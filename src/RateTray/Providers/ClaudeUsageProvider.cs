using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using RateTray.Configuration;
using RateTray.Localization;
using RateTray.Model;

namespace RateTray.Providers;

/// <summary>
/// Reads Claude subscription limits from the OAuth usage endpoint — the same data the
/// <c>/usage</c> command shows. The bearer token is the one Claude Code already stores in
/// <c>~/.claude/.credentials.json</c>; we re-read it on every poll because Claude Code
/// refreshes it in place while it runs.
/// </summary>
public sealed class ClaudeUsageProvider(ClaudeOptions options) : IUsageProvider
{
    /// <summary>
    /// Shared, with its own timeout disabled: the per-request deadline comes from
    /// <see cref="ClaudeOptions.TimeoutSeconds"/> via a linked token, which a static client
    /// cannot express through <c>HttpClient.Timeout</c>. Every call through it therefore has to
    /// pass that token — one that only carries the shutdown token can hang indefinitely.
    /// </summary>
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public string Group => "Claude";

    public bool Enabled => options.Enabled;

    private string CredentialsPath => options.CredentialsPath is { Length: > 0 } p
        ? Environment.ExpandEnvironmentVariables(p)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    public async Task<ProviderResult> ReadAsync(CancellationToken ct)
    {
        var result = await PollAsync(ct).ConfigureAwait(false);
        return EndpointNotice() is { } notice ? result with { Notice = notice } : result;
    }

    /// <summary>
    /// Names a configured endpoint that is not the shipped one, so the choice stays visible
    /// wherever the result is shown. The token URL only counts while the refresh can actually
    /// run — pointing out a setting nothing reads would just teach people to ignore the line.
    /// </summary>
    internal string? EndpointNotice()
    {
        var hosts = new[]
        {
            Endpoint.ForeignHost(options.UsageUrl, ClaudeOptions.UsageUrlDefault),
            options.AutoRefreshToken ? Endpoint.ForeignHost(options.TokenUrl, ClaudeOptions.TokenUrlDefault) : null,
        };

        var named = hosts.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return named.Count == 0 ? null : Loc.T("note.endpoint", string.Join(", ", named));
    }

    private async Task<ProviderResult> PollAsync(CancellationToken ct)
    {
        Credentials creds;
        try
        {
            creds = ReadCredentials();
        }
        catch (FileNotFoundException)
        {
            return ProviderResult.Failed(Group, Loc.T("error.claude.noCredentials")) with
            {
                Auth = new AuthStatus { Group = Group, IsValid = false, Detail = Loc.T("auth.notLoggedIn") },
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or JsonException or InvalidDataException or InvalidOperationException)
        {
            return ProviderResult.Failed(Group, Loc.T("error.claude.unreadable", ex.Message));
        }

        var auth = AuthFrom(creds);

        // Checked before the token is anywhere near a socket: over plain http it would be on
        // the wire in clear, and a poll that fails loudly is the only way a typo in a URL that
        // carries a credential becomes visible at all.
        if (!Endpoint.IsSecure(options.UsageUrl))
            return ProviderResult.Failed(Group, Loc.T("error.claude.insecureUrl", options.UsageUrl)) with { Auth = auth };

        // One deadline for the whole poll, refresh included. The shared client has no timeout of
        // its own, so a token endpoint that accepts the connection and then says nothing would
        // block this task forever — and with it the guard that keeps polls from stacking up,
        // which no later poll and no manual refresh could clear short of a restart.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds)));

        try
        {
            if (creds.IsExpired)
            {
                if (!options.AutoRefreshToken)
                    return ProviderResult.Failed(Group, Loc.T("error.claude.expired")) with { Auth = auth };

                // The refresh token is the more valuable of the two and goes to a second,
                // separately configured URL, so it gets the same check rather than inheriting
                // the one above.
                if (!Endpoint.IsSecure(options.TokenUrl))
                    return ProviderResult.Failed(Group, Loc.T("error.claude.insecureUrl", options.TokenUrl)) with { Auth = auth };

                var refreshed = await TryRefreshAsync(creds, deadline.Token).ConfigureAwait(false);
                if (refreshed is null)
                    return ProviderResult.Failed(Group, Loc.T("error.claude.refreshFailed")) with { Auth = auth };

                creds = refreshed;
                auth = AuthFrom(creds);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, options.UsageUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.AccessToken);
            request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.UserAgent.ParseAdd("RateTray/0.1");

            using var response = await Http.SendAsync(request, deadline.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return ProviderResult.Failed(Group, Loc.T("error.claude.rejected")) with
                {
                    Auth = auth with { IsValid = false },
                };
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return ProviderResult.Failed(Group, Loc.T("error.claude.rateLimited")) with
                {
                    Auth = auth,
                    RetryAfter = RetryAfterOf(response),
                    RateLimited = true,
                };
            }

            if (!response.IsSuccessStatusCode)
                return ProviderResult.Failed(Group, Loc.T("error.claude.http", (int)response.StatusCode)) with { Auth = auth };

            var json = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
            return ProviderResult.Success(Group, Parse(json)) with { Auth = auth };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;                                  // shutting down, not a failure to report
        }
        catch (OperationCanceledException)
        {
            return ProviderResult.Failed(Group, Loc.T("error.claude.timeout", options.TimeoutSeconds)) with { Auth = auth };
        }
        catch (Exception ex)
        {
            return ProviderResult.Failed(Group, Loc.T("error.fetchFailed", ex.Message)) with { Auth = auth };
        }
    }

    /// <summary>
    /// Reads a Retry-After header in either accepted form. A successful response from this
    /// endpoint carries no rate-limit headers at all, so this is the only hint the server
    /// ever gives about when to come back — and it may well not send it either.
    /// </summary>
    internal static TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        if (retry is null) return null;

        if (retry.Delta is { } delta && delta > TimeSpan.Zero) return delta;

        if (retry.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) return wait;
        }

        return null;
    }

    private AuthStatus AuthFrom(Credentials creds) => new()
    {
        Group = Group,
        IsValid = !creds.IsExpired || (options.AutoRefreshToken && creds.CanRenew),
        ExpiresAt = creds.ExpiresAtUnixMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(creds.ExpiresAtUnixMs) : null,
        RenewableUntil = creds.RefreshExpiresAtUnixMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(creds.RefreshExpiresAtUnixMs)
            : null,
        Detail = creds.SubscriptionType is { Length: > 0 } sub ? $"OAuth · {sub}" : "OAuth",
    };

    /// <summary>
    /// Builds readings from the <c>limits</c> array, which already carries every window the
    /// account has (session, weekly, per-model). Extra-usage credits are appended when enabled.
    /// </summary>
    internal static List<LimitReading> Parse(string json)
    {
        var readings = new List<LimitReading>();
        var root = JsonNode.Parse(json)?.AsObject();
        if (root is null) return readings;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (root["limits"] is JsonArray limits)
        {
            foreach (var node in limits.OfType<JsonObject>())
            {
                var kind = node["kind"]?.GetValue<string>() ?? "unknown";
                var scopeName = node["scope"]?["model"]?["display_name"]?.GetValue<string>();

                var id = $"claude.{kind}";
                if (!string.IsNullOrWhiteSpace(scopeName)) id += "." + Slug(scopeName);
                // Guard against an account exposing two windows of the same kind and scope.
                var unique = id;
                for (var i = 2; !seen.Add(unique); i++) unique = $"{id}.{i}";

                readings.Add(new LimitReading
                {
                    Id = unique,
                    Label = LabelFor(kind, scopeName),
                    Group = "Claude",
                    Percent = ReadNumber(node["percent"]) ?? 0,
                    ResetsAt = ReadIsoDate(node["resets_at"]),
                    Window = WindowFor(kind),
                    Note = node["severity"]?.GetValue<string>() is { } s && !s.Equals("normal", StringComparison.OrdinalIgnoreCase)
                        ? s
                        : null,
                    IsActive = node["is_active"]?.GetValue<bool>() ?? false,
                });
            }
        }

        if (root["extra_usage"] is JsonObject extra && (extra["is_enabled"]?.GetValue<bool>() ?? false))
        {
            var currency = extra["currency"]?.GetValue<string>();
            readings.Add(new LimitReading
            {
                Id = "claude.extra_usage",
                Label = Loc.T("label.claude.extraUsage"),
                Group = "Claude",
                Percent = ReadNumber(extra["utilization"]) ?? 0,
                Note = currency is null ? null : Loc.T("note.claude.extraCurrency", currency),
            });
        }

        return Stamp(readings);
    }

    /// <summary>Numbers the readings so the palette can shade them apart.</summary>
    private static List<LimitReading> Stamp(List<LimitReading> readings) =>
        readings.Select((reading, index) => reading with { Variant = index, VariantCount = readings.Count })
            .ToList();

    private static string LabelFor(string kind, string? scope) => kind switch
    {
        "session" => Loc.T("label.claude.session"),
        "weekly_all" => Loc.T("label.claude.weeklyAll"),
        "weekly_scoped" => scope is { Length: > 0 }
            ? Loc.T("label.claude.weeklyScoped", scope)
            : Loc.T("label.claude.weeklyModel"),
        // Unknown kinds are shown raw rather than hidden, so a new server-side window is visible.
        _ => scope is { Length: > 0 } ? $"{kind} · {scope}" : kind,
    };

    private static TimeSpan? WindowFor(string kind) => kind switch
    {
        "session" => TimeSpan.FromHours(5),
        "weekly_all" or "weekly_scoped" => TimeSpan.FromDays(7),
        _ => null,
    };

    private static string Slug(string value)
    {
        var chars = value.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();
        return new string(chars).Trim('_');
    }

    private static double? ReadNumber(JsonNode? node)
    {
        if (node is null) return null;
        try { return node.GetValue<double>(); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException) { return null; }
    }

    private static DateTimeOffset? ReadIsoDate(JsonNode? node)
    {
        var raw = node?.GetValue<string>();
        return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : null;
    }

    private Credentials ReadCredentials()
    {
        var path = CredentialsPath;
        if (!File.Exists(path)) throw new FileNotFoundException(path);

        var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                   ?? throw new InvalidDataException("leere Datei");
        var oauth = root["claudeAiOauth"]?.AsObject()
                    ?? throw new InvalidDataException("claudeAiOauth fehlt");

        return new Credentials(
            oauth["accessToken"]?.GetValue<string>() ?? throw new InvalidDataException("accessToken fehlt"),
            oauth["refreshToken"]?.GetValue<string>(),
            oauth["expiresAt"]?.GetValue<long>() ?? 0)
        {
            RefreshExpiresAtUnixMs = oauth["refreshTokenExpiresAt"]?.GetValue<long>() ?? 0,
            SubscriptionType = oauth["subscriptionType"]?.GetValue<string>(),
        };
    }

    /// <summary>
    /// Exchanges the refresh token and writes the result back, preserving every other field
    /// in the file so Claude Code keeps working. Returns null on any failure; a cancelled
    /// <paramref name="ct"/> — shutdown or the poll deadline — is thrown on for the caller
    /// to tell apart.
    /// </summary>
    private async Task<Credentials?> TryRefreshAsync(Credentials creds, CancellationToken ct)
    {
        if (creds.RefreshToken is not { Length: > 0 } refreshToken) return null;

        try
        {
            using var response = await Http.PostAsJsonAsync(options.TokenUrl, new
            {
                grant_type = "refresh_token",
                refresh_token = refreshToken,
                client_id = options.ClientId,
            }, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return null;

            var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false))?.AsObject();
            if (payload?["access_token"]?.GetValue<string>() is not { Length: > 0 } access) return null;

            var newRefresh = payload["refresh_token"]?.GetValue<string>() ?? refreshToken;
            var expiresIn = payload["expires_in"]?.GetValue<long>() ?? 3600;
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToUnixTimeMilliseconds();

            WriteCredentials(access, newRefresh, expiresAt);
            return new Credentials(access, newRefresh, expiresAt);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                      or IOException or UnauthorizedAccessException
                                      or InvalidOperationException)
        {
            return null;
        }
    }

    private void WriteCredentials(string accessToken, string refreshToken, long expiresAt)
    {
        var path = CredentialsPath;
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var oauth = root["claudeAiOauth"]!.AsObject();

        oauth["accessToken"] = accessToken;
        oauth["refreshToken"] = refreshToken;
        oauth["expiresAt"] = expiresAt;

        var temp = path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }

    private sealed record Credentials(string AccessToken, string? RefreshToken, long ExpiresAtUnixMs)
    {
        public long RefreshExpiresAtUnixMs { get; init; }

        public string? SubscriptionType { get; init; }

        /// <summary>A minute of slack keeps us from racing the expiry during a poll.</summary>
        public bool IsExpired =>
            ExpiresAtUnixMs > 0 &&
            DateTimeOffset.FromUnixTimeMilliseconds(ExpiresAtUnixMs) <= DateTimeOffset.UtcNow.AddMinutes(1);

        /// <summary>True while the refresh token itself is still good for a renewal.</summary>
        public bool CanRenew =>
            RefreshToken is { Length: > 0 } &&
            (RefreshExpiresAtUnixMs == 0 ||
             DateTimeOffset.FromUnixTimeMilliseconds(RefreshExpiresAtUnixMs) > DateTimeOffset.UtcNow);
    }
}
