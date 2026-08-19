using RateTray.Providers;

namespace RateTray.Tests;

public class PollSchedulerTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_provider_that_has_not_failed_is_always_polled()
    {
        var schedule = new PollScheduler();

        Assert.True(schedule.ShouldPoll("Claude", Start));
        Assert.Null(schedule.RetryAt("Claude"));
    }

    [Fact]
    public void A_failure_pauses_the_provider()
    {
        var schedule = new PollScheduler();

        var retryAt = schedule.RecordFailure("Claude", null, Start, refreshSeconds: 90);

        Assert.Equal(Start.AddSeconds(180), retryAt);
        Assert.False(schedule.ShouldPoll("Claude", Start.AddSeconds(179)));
        Assert.True(schedule.ShouldPoll("Claude", Start.AddSeconds(180)));
    }

    /// <summary>
    /// The regression that made a rate-limited provider disappear for good: skipped cycles were
    /// being counted as failures, so every tick pushed the deadline further out from "now" and
    /// the provider was never polled again.
    /// </summary>
    [Fact]
    public void Waiting_out_a_pause_does_not_extend_it()
    {
        var schedule = new PollScheduler();
        schedule.RecordFailure("Claude", null, Start, refreshSeconds: 90);

        // Six timer ticks pass while the provider is paused. None of them reaches it, so none
        // of them may touch the failure count.
        for (var tick = 1; tick <= 6; tick++)
        {
            var now = Start.AddSeconds(90d * tick);
            if (!schedule.ShouldPoll("Claude", now)) continue;

            Assert.Equal(2, tick);                        // first tick at or after +180 s
            Assert.Equal(1, schedule.Failures("Claude")); // still exactly one recorded failure
            return;
        }

        Assert.Fail("the provider was never polled again");
    }

    [Fact]
    public void Consecutive_failures_lengthen_the_pause()
    {
        var schedule = new PollScheduler();

        var first = schedule.RecordFailure("Claude", null, Start, 90);
        var second = schedule.RecordFailure("Claude", null, Start, 90);
        var third = schedule.RecordFailure("Claude", null, Start, 90);

        Assert.Equal(Start.AddSeconds(180), first);
        Assert.Equal(Start.AddSeconds(360), second);
        Assert.Equal(Start.AddSeconds(720), third);
    }

    [Fact]
    public void Success_clears_the_pause_and_the_failure_count()
    {
        var schedule = new PollScheduler();
        schedule.RecordFailure("Claude", null, Start, 90);

        schedule.RecordSuccess("Claude", Start);

        Assert.True(schedule.ShouldPoll("Claude", Start));
        Assert.Null(schedule.RetryAt("Claude"));
        Assert.Equal(0, schedule.Failures("Claude"));
    }

    [Fact]
    public void Providers_are_paused_independently()
    {
        var schedule = new PollScheduler();

        schedule.RecordFailure("Claude", null, Start, 90);

        Assert.False(schedule.ShouldPoll("Claude", Start));
        Assert.True(schedule.ShouldPoll("Codex", Start));
    }

    [Fact]
    public void Reset_releases_every_pause()
    {
        var schedule = new PollScheduler();
        schedule.RecordFailure("Claude", null, Start, 90);
        schedule.RecordFailure("Codex", null, Start, 90);

        schedule.Reset();

        Assert.True(schedule.ShouldPoll("Claude", Start));
        Assert.True(schedule.ShouldPoll("Codex", Start));
    }

    [Fact]
    public void After_a_reset_the_backoff_starts_over_rather_than_where_it_left_off()
    {
        var schedule = new PollScheduler();
        for (var i = 0; i < 4; i++) schedule.RecordFailure("Claude", null, Start, 90);

        schedule.Reset();
        var retryAt = schedule.RecordFailure("Claude", null, Start, 90);

        Assert.Equal(Start.AddSeconds(180), retryAt);
    }

    // ---------------------------------------------------------- minimum spacing

    /// <summary>
    /// The reason this exists: the usage endpoint has a request quota, the tray was asking it
    /// every ninety seconds for numbers that move in hours, and the 429 that earned cost the
    /// display a quarter of an hour at a time.
    /// </summary>
    [Fact]
    public void A_provider_with_a_minimum_gap_is_skipped_until_it_has_passed()
    {
        var schedule = new PollScheduler();
        schedule.SetMinInterval("Claude", TimeSpan.FromMinutes(5));

        schedule.RecordSuccess("Claude", Start);

        Assert.False(schedule.ShouldPoll("Claude", Start.AddSeconds(90)));
        Assert.False(schedule.ShouldPoll("Claude", Start.AddSeconds(299)));
        Assert.True(schedule.ShouldPoll("Claude", Start.AddSeconds(300)));
    }

    [Fact]
    public void The_gap_is_measured_from_a_failed_poll_too()
    {
        // Whether the answer was usable does not change what the request cost.
        var schedule = new PollScheduler();
        schedule.SetMinInterval("Claude", TimeSpan.FromMinutes(30));

        schedule.RecordFailure("Claude", null, Start, refreshSeconds: 90);

        Assert.False(schedule.ShouldPoll("Claude", Start.AddMinutes(29)));
        Assert.True(schedule.ShouldPoll("Claude", Start.AddMinutes(30)));
    }

    [Fact]
    public void Skipping_for_the_gap_does_not_count_as_a_failure()
    {
        var schedule = new PollScheduler();
        schedule.SetMinInterval("Claude", TimeSpan.FromMinutes(5));
        schedule.RecordSuccess("Claude", Start);

        for (var tick = 1; tick <= 3; tick++) schedule.ShouldPoll("Claude", Start.AddSeconds(90d * tick));

        Assert.Equal(0, schedule.Failures("Claude"));
        Assert.Null(schedule.RetryAt("Claude"));
    }

    [Fact]
    public void A_provider_without_a_gap_is_polled_every_cycle()
    {
        var schedule = new PollScheduler();
        schedule.RecordSuccess("Codex", Start);

        Assert.True(schedule.ShouldPoll("Codex", Start));
    }

    [Fact]
    public void The_first_poll_is_never_held_back()
    {
        var schedule = new PollScheduler();
        schedule.SetMinInterval("Claude", TimeSpan.FromHours(1));

        Assert.True(schedule.ShouldPoll("Claude", Start));
        Assert.Null(schedule.PolledAt("Claude"));
    }

    [Fact]
    public void An_explicit_refresh_beats_the_gap()
    {
        // Someone asking for numbers now is entitled to the request; the floor is there to
        // stop the timer spending the quota, not to overrule the person watching.
        var schedule = new PollScheduler();
        schedule.SetMinInterval("Claude", TimeSpan.FromMinutes(5));
        schedule.RecordSuccess("Claude", Start);

        schedule.Reset();

        Assert.True(schedule.ShouldPoll("Claude", Start));
    }

    [Fact]
    public void The_gap_survives_a_reset_because_it_is_configuration()
    {
        var schedule = new PollScheduler();
        schedule.SetMinInterval("Claude", TimeSpan.FromMinutes(5));

        schedule.Reset();
        schedule.RecordSuccess("Claude", Start);

        Assert.False(schedule.ShouldPoll("Claude", Start.AddMinutes(4)));
    }

    [Fact]
    public void A_backoff_longer_than_the_gap_still_wins()
    {
        var schedule = new PollScheduler();
        schedule.SetMinInterval("Claude", TimeSpan.FromMinutes(1));

        schedule.RecordFailure("Claude", null, Start, refreshSeconds: 90, rateLimited: true);

        Assert.False(schedule.ShouldPoll("Claude", Start.AddMinutes(14)));
        Assert.True(schedule.ShouldPoll("Claude", Start.AddMinutes(15)));
    }

    // ----------------------------------------------------------------- backoff

    [Fact]
    public void The_pause_is_capped()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), PollScheduler.Backoff(null, 20, refreshSeconds: 300));
    }

    [Fact]
    public void The_cap_is_configurable()
    {
        var schedule = new PollScheduler(maxBackoffMinutes: 2);

        var retryAt = schedule.RecordFailure("Claude", null, Start, refreshSeconds: 300);

        Assert.Equal(Start.AddMinutes(2), retryAt);
    }

    [Fact]
    public void A_server_stated_delay_wins_over_the_guess()
    {
        Assert.Equal(TimeSpan.FromSeconds(42), PollScheduler.Backoff(TimeSpan.FromSeconds(42), 4));
    }

    [Fact]
    public void A_server_stated_delay_is_capped_too()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), PollScheduler.Backoff(TimeSpan.FromHours(3), 1));
    }

    [Fact]
    public void A_nonsensical_stated_delay_falls_back_to_the_guess()
    {
        Assert.Equal(TimeSpan.FromSeconds(180), PollScheduler.Backoff(TimeSpan.Zero, 1, refreshSeconds: 90));
    }

    [Fact]
    public void The_pause_is_never_shorter_than_one_poll_interval()
    {
        foreach (var failures in (int[])[0, 1, 2, 5, 99])
            Assert.True(PollScheduler.Backoff(null, failures, 90) >= TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void A_rate_limit_earns_the_full_pause_on_the_first_refusal()
    {
        // Climbing the ladder from three minutes only spends the remaining allowance on
        // refusals, which is how the quota stays exhausted.
        var backoff = PollScheduler.Backoff(null, consecutiveFailures: 1, refreshSeconds: 90, rateLimited: true);

        Assert.Equal(TimeSpan.FromMinutes(15), backoff);
    }

    [Fact]
    public void A_rate_limit_still_yields_to_a_shorter_stated_delay()
    {
        var backoff = PollScheduler.Backoff(TimeSpan.FromSeconds(30), 1, rateLimited: true);

        Assert.Equal(TimeSpan.FromSeconds(30), backoff);
    }

    [Fact]
    public void An_ordinary_failure_is_not_treated_as_a_rate_limit()
    {
        Assert.Equal(TimeSpan.FromSeconds(180), PollScheduler.Backoff(null, 1, 90, rateLimited: false));
    }
}
