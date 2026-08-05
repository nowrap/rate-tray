namespace RateTray.Configuration;

public sealed class AppConfig
{
    /// <summary>
    /// Poll interval. The windows being watched move in hours and days, so anything under a
    /// minute buys nothing — and the Claude usage endpoint rate-limits, which a tight loop
    /// will eventually run into.
    /// </summary>
    public int RefreshSeconds { get; set; } = 90;

    /// <summary>auto | light | dark — decides the tray icon's base colour.</summary>
    public string Theme { get; set; } = "auto";

    /// <summary>auto | en | de — "auto" follows the Windows UI language.</summary>
    public string Language { get; set; } = "auto";

    public string FontFamily { get; set; } = "Segoe UI";

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

    public ThresholdOptions Thresholds { get; set; } = new();
    public NotificationOptions Notifications { get; set; } = new();
    public ClaudeOptions Claude { get; set; } = new();
    public CodexOptions Codex { get; set; } = new();
}

public sealed class ThresholdOptions
{
    /// <summary>At or above this percentage the value turns amber.</summary>
    public int Warn { get; set; } = 75;

    /// <summary>At or above this percentage the value turns red.</summary>
    public int Critical { get; set; } = 90;
}

/// <summary>
/// Hex colours (#RRGGBB). Below the warn threshold a value is drawn in its service colour,
/// which is what tells the tray icons apart; from the warn threshold on, the severity colour
/// takes over for both services so a warning never depends on knowing the service palette.
/// </summary>
public sealed class ColorOptions
{
    /// <summary>Claude's terracotta accent.</summary>
    public string Claude { get; set; } = "#D97757";

    /// <summary>OpenAI's green accent.</summary>
    public string Codex { get; set; } = "#10A37F";

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
    public double ShadeSpread { get; set; } = 0.15;
}

public sealed class NotificationOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Toast fires once per window when usage crosses this percentage.</summary>
    public int AtPercent { get; set; } = 80;
}

public sealed class ClaudeOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Defaults to %USERPROFILE%\.claude\.credentials.json.</summary>
    public string? CredentialsPath { get; set; }

    public string UsageUrl { get; set; } = "https://api.anthropic.com/api/oauth/usage";

    /// <summary>How long to wait for the usage endpoint before giving up on a poll.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Off by default: while Claude Code runs it keeps the token fresh on disk and we
    /// simply re-read it. Turning this on lets the tray refresh the OAuth token itself
    /// (and rewrite .credentials.json) so it keeps working when Claude Code is closed.
    /// </summary>
    public bool AutoRefreshToken { get; set; }

    public string TokenUrl { get; set; } = "https://console.anthropic.com/v1/oauth/token";

    /// <summary>Configurable so a changed client id can be fixed without a rebuild.</summary>
    public string ClientId { get; set; } = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
}

public sealed class CodexOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Defaults to resolving "codex" on PATH.</summary>
    public string? ExecutablePath { get; set; }

    public int TimeoutSeconds { get; set; } = 30;
}
