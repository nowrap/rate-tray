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

    /// <summary>False while the provider is backed off. Only a real poll may follow a true.</summary>
    public bool ShouldPoll(string group, DateTimeOffset now) =>
        !_retryAt.TryGetValue(group, out var until) || now >= until;

    /// <summary>When the provider will be tried again, or null if it is not backed off.</summary>
    public DateTimeOffset? RetryAt(string group) =>
        _retryAt.TryGetValue(group, out var until) ? until : null;

    public int Failures(string group) => _failures.GetValueOrDefault(group);

    public void RecordSuccess(string group)
    {
        _failures.Remove(group);
        _retryAt.Remove(group);
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

        var until = now + Backoff(statedDelay, failures, refreshSeconds, maxBackoffMinutes, rateLimited);
        _retryAt[group] = until;
        return until;
    }

    /// <summary>Clears every pause, so an explicit refresh reaches all providers at once.</summary>
    public void Reset()
    {
        _failures.Clear();
        _retryAt.Clear();
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
