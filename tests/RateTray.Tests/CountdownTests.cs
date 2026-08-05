using RateTray.Ui;

namespace RateTray.Tests;

/// <summary>The sweep of the refresh strip along the bottom of the details window.</summary>
public class CountdownTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    [Theory]
    [InlineData(60, 0.0)]
    [InlineData(45, 0.25)]
    [InlineData(30, 0.5)]
    [InlineData(15, 0.75)]
    [InlineData(0, 1.0)]
    public void Progress_tracks_the_interval(int secondsRemaining, double expected)
    {
        var progress = DetailsForm.CountdownProgress(Now.AddSeconds(secondsRemaining), Interval, Now);

        Assert.Equal(expected, progress, 3);
    }

    [Fact]
    public void An_overdue_poll_shows_a_full_bar_rather_than_wrapping_around()
    {
        // A poll in flight, or one the timer ran late on, must not reset the strip to empty.
        Assert.Equal(1.0, DetailsForm.CountdownProgress(Now.AddSeconds(-30), Interval, Now));
    }

    [Fact]
    public void A_next_poll_further_out_than_the_interval_shows_an_empty_bar()
    {
        // Happens after a clock change or when the interval was just shortened in settings.
        Assert.Equal(0.0, DetailsForm.CountdownProgress(Now.AddSeconds(600), Interval, Now));
    }

    [Fact]
    public void A_zero_interval_does_not_divide_by_zero()
    {
        Assert.Equal(0.0, DetailsForm.CountdownProgress(Now.AddSeconds(5), TimeSpan.Zero, Now));
    }

    [Fact]
    public void Progress_stays_within_range_for_any_input()
    {
        foreach (var offset in (int[])[-3600, -1, 0, 1, 59, 60, 61, 3600])
        {
            var progress = DetailsForm.CountdownProgress(Now.AddSeconds(offset), Interval, Now);
            Assert.InRange(progress, 0.0, 1.0);
        }
    }
}
