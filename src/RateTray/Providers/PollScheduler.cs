using RateTray.Model;

namespace RateTray.Providers;

/// <summary>
/// Decides when a provider may be polled again after it failed, and keeps the failure count
/// that drives the backoff.
///
/// Separate from <c>TrayApp</c> because the interesting behaviour here is a state machine, and
/// getting it subtly wrong is invisible from the outside: an earlier version ran the skipped
/// cycles through the failure accounting too, so every tick pushed the deadline further into
/// the future and the provider was never retried at all.
/// </summary>
public sealed class PollScheduler(int maxBackoffMinutes = 15)
{
    private readonly Dictionary<string, int> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _retryAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TimeSpan> _minInterval = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _polledAt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Sets a floor under how often this provider is asked, independent of the refresh
    /// interval and of whether it is failing. Configuration rather than state: it survives
    /// <see cref="Reset"/>, which only releases what a failure or a poll put there.
    /// </summary>
    public void SetMinInterval(string group, TimeSpan interval) =>
        _minInterval[group] = interval > TimeSpan.Zero ? interval : TimeSpan.Zero;

    /// <summary>
    /// False while the provider is backed off, and false again until its minimum gap has
    /// passed. Only a real poll may follow a true.
    /// </summary>
    public bool ShouldPoll(string group, DateTimeOffset now)
    {
        if (_retryAt.TryGetValue(group, out var until) && now < until) return false;

        return !_polledAt.TryGetValue(group, out var last)
               || now >= last + _minInterval.GetValueOrDefault(group);
    }

    /// <summary>When the provider will be tried again, or null if it is not backed off.</summary>
    public DateTimeOffset? RetryAt(string group) =>
        _retryAt.TryGetValue(group, out var until) ? until : null;

    public int Failures(string group) => _failures.GetValueOrDefault(group);

    /// <summary>When this provider was last actually reached, or null if it never was.</summary>
    public DateTimeOffset? PolledAt(string group) =>
        _polledAt.TryGetValue(group, out var last) ? last : null;

    public void RecordSuccess(string group, DateTimeOffset now)
    {
        _failures.Remove(group);
        _retryAt.Remove(group);
        _polledAt[group] = now;
    }

    /// <summary>
    /// Records one failed poll and returns when to try again. Call this only for cycles that
    /// actually reached the provider — counting a skipped cycle would extend the very pause
    /// that caused the skip.
    /// </summary>
    public DateTimeOffset RecordFailure(string group, TimeSpan? statedDelay, DateTimeOffset now,
        int refreshSeconds, bool rateLimited = false)
    {
        var failures = _failures.GetValueOrDefault(group) + 1;
        _failures[group] = failures;
        _polledAt[group] = now;

        var until = now + Backoff(statedDelay, failures, refreshSeconds, maxBackoffMinutes, rateLimited);
        _retryAt[group] = until;
        return until;
    }

    /// <summary>
    /// Clears every pause — the backoff and the minimum gap alike — so an explicit refresh
    /// reaches all providers at once. Someone who asks for numbers now is entitled to the
    /// request, even where the tray would have spaced it out on its own.
    /// </summary>
    public void Reset()
    {
        _failures.Clear();
        _retryAt.Clear();
        _polledAt.Clear();
    }

    /// <summary>
    /// How long to wait after a failure. A server-stated delay wins; otherwise the wait doubles
    /// with each consecutive failure, capped so a service that recovers overnight is picked up.
    /// </summary>
    public static TimeSpan Backoff(TimeSpan? statedDelay, int consecutiveFailures, int refreshSeconds = 90,
        int maxBackoffMinutes = 15, bool rateLimited = false)
    {
        var cap = TimeSpan.FromMinutes(Math.Max(1, maxBackoffMinutes));

        if (statedDelay is { } stated && stated > TimeSpan.Zero)
            return stated < cap ? stated : cap;

        // A quota says "not now" about the next while, not about this instant. Climbing the
        // ladder from three minutes only spends the allowance on refusals.
        if (rateLimited) return cap;

        var seconds = Math.Max(1, refreshSeconds) * Math.Pow(2, Math.Clamp(consecutiveFailures, 1, 5));
        return TimeSpan.FromSeconds(Math.Min(seconds, cap.TotalSeconds));
    }
}
