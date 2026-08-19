using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RateTray.Configuration;
using RateTray.Localization;
using RateTray.Model;

namespace RateTray.Providers;

/// <summary>
/// Reads Codex limits through <c>codex app-server</c>, the CLI's stdio JSON-RPC surface,
/// via <c>account/rateLimits/read</c>. This returns live server-side numbers and costs no
/// model tokens — unlike scraping the last <c>token_count</c> event out of a session rollout,
/// which only reflects whenever Codex last ran.
///
/// The server is spawned per poll and killed afterwards: a short-lived process cannot wedge
/// and needs no reconnect logic, and at a 60 s interval the startup cost is irrelevant.
/// </summary>
public sealed class CodexUsageProvider(CodexOptions options) : IUsageProvider
{
    private const int InitializeId = 1;
    private const int RateLimitsId = 2;

    public string Group => "Codex";

    public bool Enabled => options.Enabled;

    private static string AuthPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "auth.json");

    public async Task<ProviderResult> ReadAsync(CancellationToken ct)
    {
        var auth = ReadAuthStatus();

        var exe = ResolveExecutable();
        if (exe is null)
            return ProviderResult.Failed(Group, Loc.T("error.codex.notFound")) with { Auth = auth };

        if (auth is { IsValid: false })
            return ProviderResult.Failed(Group, Loc.T("error.codex.expired")) with { Auth = auth };

        try
        {
            var json = await CallAsync(exe, ct).ConfigureAwait(false);
            var readings = Parse(json, out var planType);

            if (planType is { Length: > 0 } && auth is not null)
                auth = auth with { Detail = $"{auth.Detail} · {planType}" };

            return readings.Count == 0
                ? ProviderResult.Failed(Group, Loc.T("error.codex.noLimits")) with { Auth = auth }
                : ProviderResult.Success(Group, readings) with { Auth = auth };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return ProviderResult.Failed(Group, Loc.T("error.codex.timeout", options.TimeoutSeconds)) with { Auth = auth };
        }
        catch (CodexServerException ex) when (ex.TokenExpired)
        {
            // The server refused the very token the auth file says is good for days yet — an
            // access token can be revoked long before its `exp`. Between the two, the refusal
            // is the one that is true, so the login is reported as expired rather than leaving
            // "valid until Sunday" next to a 401 nobody can act on.
            return ProviderResult.Failed(Group, Loc.T("error.codex.expired"))
                with { Auth = auth is null ? null : auth with { IsValid = false } };
        }
        catch (Exception ex)
        {
            return ProviderResult.Failed(Group, Loc.T("error.fetchFailed", ex.Message)) with { Auth = auth };
        }
    }

    /// <summary>
    /// Runs one initialize → account/rateLimits/read round trip. stdin stays open until the
    /// answer arrives; closing it early makes the server exit before it has replied.
    /// </summary>
    private async Task<JsonObject> CallAsync(string exe, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(exe, "app-server")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException(Loc.T("error.codex.startFailed"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds)));

        // Drain stderr so a chatty server can never fill its pipe buffer and stall.
        var stderr = Task.Run(() => process.StandardError.ReadToEndAsync(CancellationToken.None), CancellationToken.None);

        try
        {
            await process.StandardInput.WriteLineAsync(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientInfo":{"name":"RateTray","version":"0.1.0"}}}""").ConfigureAwait(false);
            await process.StandardInput.WriteLineAsync(
                """{"jsonrpc":"2.0","method":"initialized","params":{}}""").ConfigureAwait(false);
            await process.StandardInput.WriteLineAsync(
                """{"jsonrpc":"2.0","id":2,"method":"account/rateLimits/read","params":{}}""").ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);

            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                if (line is null) break;                       // server exited without answering
                if (line.Length == 0) continue;

                if (JsonNode.Parse(line) is not JsonObject message) continue;
                if (ReadId(message) != RateLimitsId) continue; // skip notifications and the initialize reply

                if (message["error"] is JsonObject err)
                    throw new CodexServerException(err["message"]?.GetValue<string>() ?? Loc.T("error.codex.rpcUnknown"));

                return message["result"] as JsonObject
                       ?? throw new InvalidOperationException(Loc.T("error.codex.noResult"));
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException();
        }
        finally
        {
            TryKill(process);
        }

        var diagnostics = await SafeAwait(stderr).ConfigureAwait(false);
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(diagnostics) ? Loc.T("error.codex.noAnswer") : diagnostics.Trim());
    }

    internal static List<LimitReading> Parse(JsonObject result, out string? planType)
    {
        planType = null;
        var readings = new List<LimitReading>();

        // `rateLimits` is the single-bucket view; `rateLimitsByLimitId` may carry more.
        if (result["rateLimits"] is JsonObject primaryBucket)
        {
            planType = primaryBucket["planType"]?.GetValue<string>();
            AddWindows(readings, primaryBucket, "codex", planType);
        }

        if (result["rateLimitsByLimitId"] is JsonObject buckets)
        {
            foreach (var (limitId, node) in buckets)
            {
                // The default bucket is already covered by `rateLimits` above.
                if (node is not JsonObject bucket || limitId.Equals("codex", StringComparison.OrdinalIgnoreCase)) continue;
                AddWindows(readings, bucket, $"codex.{Slug(limitId)}", bucket["planType"]?.GetValue<string>());
            }
        }

        // Numbered so the palette can shade readings of the same service apart.
        return readings
            .Select((reading, index) => reading with { Variant = index, VariantCount = readings.Count })
            .ToList();
    }

    /// <summary>
    /// Turns one bucket's windows into readings. Every bucket but the account-wide one belongs
    /// to a single model and names it in <c>limitName</c>; without that name a plan with a
    /// per-model quota shows two rows both reading "Codex · Week", and the one counting a model
    /// that has not been used yet looks like the same limit refusing to move.
    /// </summary>
    private static void AddWindows(List<LimitReading> readings, JsonObject bucket, string prefix, string? planType)
    {
        var accountWide = prefix == "codex";
        var limitName = bucket["limitName"]?.GetValue<string>();

        foreach (var slot in (string[])["primary", "secondary"])
        {
            if (bucket[slot] is not JsonObject window) continue;
            if (window["usedPercent"] is not { } percentNode) continue;

            var minutes = window["windowDurationMins"]?.GetValue<long?>();
            var span = minutes is > 0 ? TimeSpan.FromMinutes(minutes.Value) : (TimeSpan?)null;
            var resets = window["resetsAt"]?.GetValue<long?>();
            var windowText = span is { } s ? LimitReading.FormatWindow(s) : slot;

            readings.Add(new LimitReading
            {
                Id = accountWide ? $"codex.{slot}" : $"{prefix}.{slot}",
                // "Week · <model>", the shape Claude's per-model window already uses, and
                // without the service prefix on purpose: "Codex · Week · GPT-5.3-Codex-Spark"
                // is wider than the label column, so the ellipsis would eat the model name —
                // the only part that tells the two weekly rows apart.
                Label = limitName is { Length: > 0 } name
                    ? Loc.T("label.codex.windowNamed", windowText, name)
                    : Loc.T("label.codex.window", windowText),
                Group = "Codex",
                Percent = percentNode.GetValue<double>(),
                ResetsAt = resets is > 0 ? DateTimeOffset.FromUnixTimeSeconds(resets.Value) : null,
                Window = span,
                Note = planType,
                // Only the account-wide window is the one being spent right now: a model's own
                // limit moves solely while that model runs, and the server does not say whether
                // it is. Marking both as active made the two indistinguishable rows worse.
                IsActive = slot == "primary" && accountWide,
            });
        }
    }

    /// <summary>
    /// Derives login validity from the ChatGPT access token in ~/.codex/auth.json. The
    /// id_token expires after an hour and is not what requests are authorised with, so
    /// only the access token's <c>exp</c> claim is meaningful here.
    /// </summary>
    private AuthStatus? ReadAuthStatus()
    {
        try
        {
            if (!File.Exists(AuthPath))
                return new AuthStatus { Group = Group, IsValid = false, Detail = Loc.T("auth.notLoggedIn") };

            var root = JsonNode.Parse(File.ReadAllText(AuthPath))?.AsObject();
            var mode = root?["auth_mode"]?.GetValue<string>();

            if (root?["OPENAI_API_KEY"]?.GetValue<string>() is { Length: > 0 } && mode is not "chatgpt")
                return new AuthStatus { Group = Group, IsValid = true, Detail = Loc.T("auth.apiKey") };

            var accessToken = root?["tokens"]?["access_token"]?.GetValue<string>();
            if (accessToken is not { Length: > 0 })
                return new AuthStatus { Group = Group, IsValid = false, Detail = Loc.T("auth.notLoggedIn") };

            var expiry = JwtExpiry(accessToken);
            return new AuthStatus
            {
                Group = Group,
                IsValid = expiry is null || expiry > DateTimeOffset.UtcNow,
                ExpiresAt = expiry,
                Detail = mode is { Length: > 0 } ? mode : "chatgpt",
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static DateTimeOffset? JwtExpiry(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) return null;

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            var exp = JsonNode.Parse(json)?["exp"]?.GetValue<long>();
            return exp is > 0 ? DateTimeOffset.FromUnixTimeSeconds(exp.Value) : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException
                                      or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves codex.exe. A .cmd/.bat shim cannot be launched directly with
    /// UseShellExecute=false, so those are deliberately not returned.
    /// </summary>
    private string? ResolveExecutable()
    {
        if (options.ExecutablePath is { Length: > 0 } configured)
        {
            var expanded = Environment.ExpandEnvironmentVariables(configured);
            return File.Exists(expanded) ? expanded : null;
        }

        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "OpenAI", "Codex", "bin", "codex.exe"),
        };

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            try { candidates.Add(Path.Combine(dir, "codex.exe")); }
            catch (ArgumentException) { /* malformed PATH entry */ }
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The process can exit between HasExited and Kill; that race is the normal case.
        }
    }

    private static async Task<string> SafeAwait(Task<string> task)
    {
        try { return await task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (Exception ex) when (ex is TimeoutException or IOException or ObjectDisposedException) { return string.Empty; }
    }

    private static int? ReadId(JsonObject message)
    {
        var id = message["id"];
        if (id is null) return null;
        try { return id.GetValue<int>(); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException) { return null; }
    }

    private static string Slug(string value) =>
        new(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
}

/// <summary>
/// An error the app-server answered the RPC with. It passes the upstream HTTP body on verbatim,
/// so the raw text is regularly a whole pretty-printed JSON document — several lines of it, of
/// which exactly one sentence is addressed to a person. <see cref="Exception.Message"/> is that
/// sentence; the raw document is only searched for the code that says the login was refused.
/// </summary>
internal sealed class CodexServerException(string raw) : Exception(Summarize(raw))
{
    /// <summary>The server rejected the token itself, rather than failing to answer.</summary>
    public bool TokenExpired { get; } = raw.Contains("token_expired", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The human-readable part of a server error: the text before the embedded body, plus the
    /// body's own <c>message</c> when it has one. Anything that cannot be read as JSON keeps
    /// only the text in front of it — that is the part the app-server itself wrote.
    /// </summary>
    internal static string Summarize(string raw)
    {
        var brace = raw.IndexOf('{');
        if (brace < 0) return raw.Trim();

        var head = raw[..brace].Trim().TrimEnd(':', '-', ' ');
        var body = raw[brace..].Trim();

        string? detail = null;
        try
        {
            if (JsonNode.Parse(body) is JsonObject json)
                detail = (json["error"]?["message"] ?? json["message"])?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Not JSON after all, or a `message` that is not a string: the head still stands.
        }

        return (head, detail) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => $"{head}: {detail}",
            ({ Length: > 0 }, _) => head,
            (_, { Length: > 0 }) => detail,
            _ => raw.Trim(),
        };
    }
}
