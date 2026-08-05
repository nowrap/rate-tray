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
                {
                    var text = err["message"]?.GetValue<string>() ?? Loc.T("error.codex.rpcUnknown");
                    throw new InvalidOperationException(text);
                }

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

    private static void AddWindows(List<LimitReading> readings, JsonObject bucket, string prefix, string? planType)
    {
        foreach (var slot in (string[])["primary", "secondary"])
        {
            if (bucket[slot] is not JsonObject window) continue;
            if (window["usedPercent"] is not { } percentNode) continue;

            var minutes = window["windowDurationMins"]?.GetValue<long?>();
            var span = minutes is > 0 ? TimeSpan.FromMinutes(minutes.Value) : (TimeSpan?)null;
            var resets = window["resetsAt"]?.GetValue<long?>();

            readings.Add(new LimitReading
            {
                Id = prefix == "codex" ? $"codex.{slot}" : $"{prefix}.{slot}",
                Label = Loc.T("label.codex.window", span is { } s ? LimitReading.FormatWindow(s) : slot),
                Group = "Codex",
                Percent = percentNode.GetValue<double>(),
                ResetsAt = resets is > 0 ? DateTimeOffset.FromUnixTimeSeconds(resets.Value) : null,
                Window = span,
                Note = planType,
                IsActive = slot == "primary",
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
