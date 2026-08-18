using RateTray.Localization;

namespace RateTray.Model;

/// <summary>
/// One usage window as reported by a provider. Providers normalise their very
/// different payloads into this shape so the tray, tooltip and details window
/// never need to know which service a value came from.
/// </summary>
public sealed record LimitReading
{
    /// <summary>Stable key used in settings.json, e.g. <c>claude.weekly_all</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Short label for menus and the details window.</summary>
    public required string Label { get; init; }

    /// <summary>Provider name, used for grouping in the UI.</summary>
    public required string Group { get; init; }

    /// <summary>Percentage of the window already consumed (0..100).</summary>
    public double Percent { get; init; }

    public DateTimeOffset? ResetsAt { get; init; }

    /// <summary>Length of the rolling window, when the provider reports one.</summary>
    public TimeSpan? Window { get; init; }

    /// <summary>Extra context for the details window (plan name, model scope, ...).</summary>
    public string? Note { get; init; }

    /// <summary>True when this is the window currently governing throttling.</summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Position of this reading among its provider's readings, and how many there are. The
    /// palette shades limits of the same service apart by this, so three Claude icons are
    /// told apart by more than their position in the tray. Assigned by the provider so that
    /// tray, hover card and details window all derive the same colour.
    /// </summary>
    public int Variant { get; init; }

    public int VariantCount { get; init; } = 1;

    /// <summary>
    /// Text drawn into the tray icon: the same rounded percentage the details window and the
    /// hover card show, without a sign. A full limit reads "100" — the renderer shrinks a
    /// three-digit value to fit, and capping it at "99" instead meant the one number that has
    /// to be right disagreed with every other place the same reading is shown.
    /// Values outside 0..100 are clamped: a provider reporting 103 % is still just "full".
    /// </summary>
    public string IconText => Math.Round(Math.Clamp(Percent, 0, 100)).ToString("0");

    public string ResetText()
    {
        if (ResetsAt is not { } reset) return Loc.T("limit.resetUnknown");

        var stamp = Loc.DateTime(reset);
        var left = reset.ToLocalTime() - DateTimeOffset.Now;
        return left <= TimeSpan.Zero
            ? Loc.T("limit.resetDue", stamp)
            : Loc.T("limit.resetIn", stamp, FormatSpan(left));
    }

    public static string FormatSpan(TimeSpan span)
    {
        if (span.TotalDays >= 1) return Loc.T("span.daysHours", (int)span.TotalDays, span.Hours);
        if (span.TotalHours >= 1) return Loc.T("span.hoursMinutes", (int)span.TotalHours, span.Minutes);
        return Loc.T("span.minutes", Math.Max(1, (int)span.TotalMinutes));
    }

    public static string FormatWindow(TimeSpan window)
    {
        // Whole minutes, not fractional days. Testing a double for exact divisibility works
        // only as long as every provider happens to report a window that divides evenly —
        // the first one that does not would silently render "7 d" instead of "Week".
        const long Hour = 60, Day = 24 * Hour, Week = 7 * Day;
        var minutes = (long)Math.Round(window.TotalMinutes);

        if (minutes >= Week && minutes % Week == 0)
        {
            var weeks = minutes / Week;
            return weeks == 1 ? Loc.T("window.week") : Loc.T("window.weeks", weeks);
        }

        return minutes >= Day
            ? Loc.T("window.days", minutes / Day)
            : Loc.T("window.hours", minutes / Hour);
    }
}

/// <summary>
/// Validity of the credential a provider authenticates with. Surfaced in the details
/// window so an expiring login is visible before it starts failing.
/// </summary>
public sealed record AuthStatus
{
    public required string Group { get; init; }

    public required bool IsValid { get; init; }

    /// <summary>Expiry of the short-lived token actually used for requests.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>How long the session can still be renewed without a fresh login.</summary>
    public DateTimeOffset? RenewableUntil { get; init; }

    /// <summary>Auth mode / plan, e.g. "chatgpt · plus".</summary>
    public string? Detail { get; init; }

    public string Summary()
    {
        if (!IsValid) return Loc.T("auth.expired");
        if (ExpiresAt is not { } exp) return Loc.T("auth.valid");

        var left = exp.ToLocalTime() - DateTimeOffset.Now;
        return left <= TimeSpan.Zero
            ? Loc.T("auth.expiredRenew")
            : Loc.T("auth.validUntil", Loc.DateTime(exp), LimitReading.FormatSpan(left));
    }
}

/// <summary>Outcome of a single provider poll: either readings, or a reason why not.</summary>
public sealed record ProviderResult(string Group, IReadOnlyList<LimitReading> Readings, string? Error)
{
    public bool Ok => Error is null;

    /// <summary>Reported even when the poll failed — that is exactly when it matters.</summary>
    public AuthStatus? Auth { get; init; }

    /// <summary>
    /// Something about how this poll was made that the numbers do not show — currently a
    /// configured endpoint other than the one the app ships with. Not an error: it is a
    /// legitimate setting, and saying so is what keeps it from being an invisible one.
    /// </summary>
    public string? Notice { get; init; }

    /// <summary>
    /// How long the server asked us to wait, when it said so. Takes precedence over the
    /// caller's own backoff: a stated delay is better than a guessed one.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>
    /// When this provider will be tried again while it is backed off. Shown next to the error,
    /// because "paused" without a duration reads like "broken".
    /// </summary>
    public DateTimeOffset? RetryAt { get; init; }

    /// <summary>
    /// The server refused because of a rate limit, not because something went wrong. It earns
    /// the longest pause straight away: retrying a quota every few minutes is what keeps it
    /// exhausted, and unlike a dropped connection there is nothing to gain from trying sooner.
    /// </summary>
    public bool RateLimited { get; init; }

    public static ProviderResult Success(string group, IReadOnlyList<LimitReading> readings) =>
        new(group, readings, null);

    public static ProviderResult Failed(string group, string error) =>
        new(group, [], error);
}
