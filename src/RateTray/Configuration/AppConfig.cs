namespace RateTray.Configuration;

public sealed class AppConfig
{
    internal const string ThemeDefault = "auto";
    internal const string LanguageDefault = "auto";
    internal const string FontDefault = "Segoe UI";

    /// <summary>
    /// Poll interval. The windows being watched move in hours and days, so anything under a
    /// minute buys nothing — and the Claude usage endpoint rate-limits, which a tight loop
    /// will eventually run into.
    /// </summary>
    public int RefreshSeconds { get; set; } = 90;

    /// <summary>auto | light | dark — decides the tray icon's base colour.</summary>
    public string Theme { get; set; } = ThemeDefault;

    /// <summary>auto | en | de — "auto" follows the Windows UI language.</summary>
    public string Language { get; set; } = LanguageDefault;

    public string FontFamily { get; set; } = FontDefault;

    /// <summary>
    /// Own hover card with the service mark instead of the native text tooltip. Turn off to
    /// fall back to the plain Windows tooltip (text only, capped at 63 characters).
    /// </summary>
    public bool RichTooltips { get; set; } = true;

    public ColorOptions Colors { get; set; } = new();

    /// <summary>
    /// Ordered list of <see cref="Model.LimitReading.Id"/> values to show as tray icons.
    /// Unknown ids are ignored, so a stale config never breaks startup.
    /// </summary>
    public List<string> Icons { get; set; } = [];

    /// <summary>
    /// False until the first successful poll has filled <see cref="Icons"/> with everything
    /// the account actually reports — which windows exist differs per plan (per-model limits
    /// such as Fable only appear for some), so a hardcoded default list would silently miss them.
    /// Set to false again to re-discover.
    /// </summary>
    public bool IconsInitialized { get; set; }

    /// <summary>
    /// Longest a failing provider is left alone before the next attempt. The wait doubles with
    /// each consecutive failure until it reaches this ceiling — low enough that a service which
    /// recovers overnight is picked up again, high enough not to pester a rate-limited endpoint.
    /// </summary>
    public int MaxBackoffMinutes { get; set; } = 15;

    /// <summary>
    /// Off by default: RateTray makes no network call you did not ask for. Turn it on — in the About
    /// dialog — to check GitHub for a newer release on start-up, at most once a day. The manual
    /// "check for updates" button in that dialog works either way.
    /// </summary>
    public bool AutoUpdateCheck { get; set; } = false;

    /// <summary>When the automatic check last ran, so it is not repeated on every launch. Null
    /// until the first check.</summary>
    public DateTimeOffset? LastUpdateCheck { get; set; }

    public ThresholdOptions Thresholds { get; set; } = new();
    public NotificationOptions Notifications { get; set; } = new();
    public ClaudeOptions Claude { get; set; } = new();
    public CodexOptions Codex { get; set; } = new();

    /// <summary>
    /// Makes a hand-edited settings.json safe to use. Syntactically valid JSON still
    /// deserialises into nulls and absurd numbers — <c>"icons": null</c>, <c>"theme": null</c>,
    /// <c>"refreshSeconds": 3000000</c> — and each of those used to surface as a crash during
    /// start-up rather than as a bad setting. One pass here keeps that knowledge in a single
    /// place instead of a null check at every use, and <see cref="ConfigStore.Load"/> writes the
    /// repaired file back so the next start is clean.
    /// </summary>
    public AppConfig Normalize()
    {
        // The floor is the same reason the poll timer has one: the usage endpoint rate-limits.
        // The ceiling only has to keep seconds * 1000 inside the int the timer takes.
        RefreshSeconds = Math.Clamp(RefreshSeconds, 30, 86_400);
        MaxBackoffMinutes = Math.Clamp(MaxBackoffMinutes, 1, 1_440);

        Theme = Sane.Text(Theme, ThemeDefault);
        Language = Sane.Text(Language, LanguageDefault);
        FontFamily = Sane.Text(FontFamily, FontDefault);

        // A null id would throw on the first comparison in the icons menu.
        Icons = Icons is null
            ? []
            : Icons.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToList();

        Colors = (Colors ?? new()).Normalize();
        Thresholds = (Thresholds ?? new()).Normalize();
        Notifications = (Notifications ?? new()).Normalize();
        Claude = (Claude ?? new()).Normalize();
        Codex = (Codex ?? new()).Normalize();

        return this;
    }
}

public sealed class ThresholdOptions
{
    /// <summary>At or above this percentage the value turns amber.</summary>
    public int Warn { get; set; } = 75;

    /// <summary>At or above this percentage the value turns red.</summary>
    public int Critical { get; set; } = 90;

    /// <summary>
    /// Clamped, not reordered: warn above critical is a strange setting but a legible one —
    /// every value simply turns red a step earlier. Silently swapping the two would overrule
    /// someone who meant it.
    /// </summary>
    internal ThresholdOptions Normalize()
    {
        Warn = Math.Clamp(Warn, 0, 100);
        Critical = Math.Clamp(Critical, 0, 100);
        return this;
    }
}

/// <summary>
/// Hex colours (#RRGGBB). Below the warn threshold a value is drawn in its service colour,
/// which is what tells the tray icons apart; from the warn threshold on, the severity colour
/// takes over for both services so a warning never depends on knowing the service palette.
/// </summary>
public sealed class ColorOptions
{
    internal const string ClaudeDefault = "#D97757";
    internal const string CodexDefault = "#10A37F";
    internal const double ShadeSpreadDefault = 0.15;

    /// <summary>Claude's terracotta accent.</summary>
    public string Claude { get; set; } = ClaudeDefault;

    /// <summary>OpenAI's green accent.</summary>
    public string Codex { get; set; } = CodexDefault;

    /// <summary>
    /// Hue of the warning colour in degrees (48 = amber). The colour itself is built from this
    /// hue plus the shared saturation and lightness of the two service colours, so it stays in
    /// tune with them when they are changed.
    /// </summary>
    public int WarnHue { get; set; } = 48;

    /// <summary>Hue of the critical colour in degrees (352 = crimson).</summary>
    public int CriticalHue { get; set; } = 352;

    /// <summary>Set to a hex value to override the derived warning colour; null derives it.</summary>
    public string? Warn { get; set; }

    /// <summary>Set to a hex value to override the derived critical colour; null derives it.</summary>
    public string? Critical { get; set; }

    /// <summary>Colour for a limit with no value, e.g. after a failed poll. Null derives it.</summary>
    public string? Unknown { get; set; }

    /// <summary>
    /// How far apart limits of the same service are shaded, as a lightness range (0.15 = 15
    /// percentage points from the first limit to the last). 0 turns shading off and gives every
    /// limit of a service the identical colour. Only applies below the warning threshold.
    ///
    /// Kept modest on purpose: wide enough to tell three tray icons apart at a glance, narrow
    /// enough that the last shade still reads as the service's colour rather than a pale tint.
    /// </summary>
    public double ShadeSpread { get; set; } = ShadeSpreadDefault;

    internal ColorOptions Normalize()
    {
        Claude = Sane.Text(Claude, ClaudeDefault);
        Codex = Sane.Text(Codex, CodexDefault);
        WarnHue = Math.Clamp(WarnHue, 0, 359);
        CriticalHue = Math.Clamp(CriticalHue, 0, 359);

        // Null means "derive it", so only blanks and non-numbers are corrected here.
        Warn = Sane.Optional(Warn);
        Critical = Sane.Optional(Critical);
        Unknown = Sane.Optional(Unknown);
        ShadeSpread = double.IsFinite(ShadeSpread) ? Math.Clamp(ShadeSpread, 0, 1) : ShadeSpreadDefault;

        return this;
    }
}

public sealed class NotificationOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Toast fires once per window when usage crosses this percentage.</summary>
    public int AtPercent { get; set; } = 80;

    internal NotificationOptions Normalize()
    {
        AtPercent = Math.Clamp(AtPercent, 0, 100);
        return this;
    }
}

public sealed class ClaudeOptions
{
    internal const string UsageUrlDefault = "https://api.anthropic.com/api/oauth/usage";
    internal const string TokenUrlDefault = "https://console.anthropic.com/v1/oauth/token";
    internal const string ClientIdDefault = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    public bool Enabled { get; set; } = true;

    /// <summary>Defaults to %USERPROFILE%\.claude\.credentials.json.</summary>
    public string? CredentialsPath { get; set; }

    public string UsageUrl { get; set; } = UsageUrlDefault;

    /// <summary>How long to wait for the usage endpoint before giving up on a poll.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Shortest gap between two requests to the usage endpoint, whatever the refresh interval
    /// is set to. The endpoint has a request quota and the numbers behind it move in hours, so
    /// asking it every ninety seconds spends the allowance without ever learning anything new —
    /// and the 429 that follows costs the display for a quarter of an hour. 0 removes the floor.
    /// </summary>
    public int MinIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Off by default: while Claude Code runs it keeps the token fresh on disk and we
    /// simply re-read it. Turning this on lets the tray refresh the OAuth token itself
    /// (and rewrite .credentials.json) so it keeps working when Claude Code is closed.
    /// </summary>
    public bool AutoRefreshToken { get; set; }

    public string TokenUrl { get; set; } = TokenUrlDefault;

    /// <summary>Configurable so a changed client id can be fixed without a rebuild.</summary>
    public string ClientId { get; set; } = ClientIdDefault;

    internal ClaudeOptions Normalize()
    {
        CredentialsPath = Sane.Optional(CredentialsPath);
        UsageUrl = Sane.Text(UsageUrl, UsageUrlDefault);
        TokenUrl = Sane.Text(TokenUrl, TokenUrlDefault);
        ClientId = Sane.Text(ClientId, ClientIdDefault);
        TimeoutSeconds = Math.Clamp(TimeoutSeconds, 5, 300);
        MinIntervalSeconds = Math.Clamp(MinIntervalSeconds, 0, 3_600);
        return this;
    }
}

public sealed class CodexOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Defaults to resolving "codex" on PATH.</summary>
    public string? ExecutablePath { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    internal CodexOptions Normalize()
    {
        ExecutablePath = Sane.Optional(ExecutablePath);
        TimeoutSeconds = Math.Clamp(TimeoutSeconds, 5, 300);
        return this;
    }
}

/// <summary>
/// The two string repairs every <c>Normalize</c> above needs. Deliberately file-local: this is
/// about surviving a hand-edited file, not an API anything else should reach for.
/// </summary>
file static class Sane
{
    /// <summary>A blank value means the setting was never really set, so the default applies.</summary>
    public static string Text(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    /// <summary>For settings where null is meaningful — blank collapses to it rather than past it.</summary>
    public static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
