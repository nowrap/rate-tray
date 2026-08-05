using RateTray.Localization;
using RateTray.Model;

namespace RateTray.Tests;

public class LimitReadingTests
{
    public LimitReadingTests() => Loc.Use("en");

    private static LimitReading Reading(double percent, DateTimeOffset? reset = null) => new()
    {
        Id = "claude.weekly_all",
        Label = "Week",
        Group = "Claude",
        Percent = percent,
        ResetsAt = reset,
    };

    [Theory]
    [InlineData(0, "0")]
    [InlineData(6.4, "6")]
    [InlineData(6.6, "7")]
    [InlineData(17, "17")]
    public void Icon_text_is_the_rounded_percentage(double percent, string expected)
    {
        Assert.Equal(expected, Reading(percent).IconText);
    }

    [Theory]
    [InlineData(99.5)]
    [InlineData(99.9)]
    [InlineData(100)]
    public void A_full_limit_shows_99_because_three_digits_do_not_fit_an_icon(double percent)
    {
        Assert.Equal("99", Reading(percent).IconText);
    }

    [Fact]
    public void Reset_text_reports_the_remaining_time()
    {
        var text = Reading(10, DateTimeOffset.Now.AddHours(3).AddMinutes(20)).ResetText();

        Assert.Contains("3 h", text);
    }

    [Fact]
    public void A_reset_in_the_past_is_flagged_as_due()
    {
        var text = Reading(10, DateTimeOffset.Now.AddMinutes(-5)).ResetText();

        Assert.Contains("due", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_reset_is_stated_rather_than_faked()
    {
        Assert.Equal(Loc.T("limit.resetUnknown"), Reading(10).ResetText());
    }

    [Theory]
    [InlineData(0, 45, "45 min")]
    [InlineData(0, 0, "1 min")]        // sub-minute rounds up, never to "0 min"
    [InlineData(5, 30, "5 h 30 min")]
    public void Short_spans_are_formatted_in_hours_and_minutes(int hours, int minutes, string expected)
    {
        Assert.Equal(expected, LimitReading.FormatSpan(new TimeSpan(hours, minutes, 0)));
    }

    [Fact]
    public void Long_spans_are_formatted_in_days_and_hours()
    {
        Assert.Equal("5 d 17 h", LimitReading.FormatSpan(new TimeSpan(5, 17, 30, 0)));
    }

    [Theory]
    [InlineData(7, "Week")]
    [InlineData(14, "2 weeks")]
    [InlineData(1, "1 d")]
    public void Windows_of_whole_days_are_named(int days, string expected)
    {
        Assert.Equal(expected, LimitReading.FormatWindow(TimeSpan.FromDays(days)));
    }

    [Fact]
    public void A_sub_day_window_is_shown_in_hours()
    {
        Assert.Equal("5 h", LimitReading.FormatWindow(TimeSpan.FromHours(5)));
    }

    [Fact]
    public void A_window_that_is_not_a_whole_number_of_weeks_falls_back_to_days()
    {
        // 10080 minutes is exactly a week; one minute more must not be rounded into one.
        Assert.Equal(Loc.T("window.week"), LimitReading.FormatWindow(TimeSpan.FromMinutes(10080)));
        Assert.Equal("7 d", LimitReading.FormatWindow(TimeSpan.FromMinutes(10081)));
    }

    [Fact]
    public void Week_detection_does_not_rely_on_floating_point_equality()
    {
        // Built from minutes rather than days, the way a provider reports it. The old
        // implementation compared TotalDays % 7 to zero and was one rounding away from
        // rendering this as "7 d".
        foreach (var weeks in (int[])[1, 2, 4])
        {
            var window = TimeSpan.FromMinutes(weeks * 7 * 24 * 60);
            var expected = weeks == 1 ? Loc.T("window.week") : Loc.T("window.weeks", weeks);

            Assert.Equal(expected, LimitReading.FormatWindow(window));
        }
    }

    [Fact]
    public void Formatting_follows_the_active_language()
    {
        Loc.Use("de");
        Assert.Equal("Woche", LimitReading.FormatWindow(TimeSpan.FromDays(7)));

        Loc.Use("en");
        Assert.Equal("Week", LimitReading.FormatWindow(TimeSpan.FromDays(7)));
    }
}

public class AuthStatusTests
{
    public AuthStatusTests() => Loc.Use("en");

    [Fact]
    public void An_invalid_login_asks_for_a_new_sign_in()
    {
        var status = new AuthStatus { Group = "Codex", IsValid = false };

        Assert.Equal(Loc.T("auth.expired"), status.Summary());
    }

    [Fact]
    public void A_valid_login_without_an_expiry_is_simply_valid()
    {
        var status = new AuthStatus { Group = "Codex", IsValid = true };

        Assert.Equal(Loc.T("auth.valid"), status.Summary());
    }

    [Fact]
    public void A_valid_login_reports_how_long_it_lasts()
    {
        var status = new AuthStatus
        {
            Group = "Claude",
            IsValid = true,
            ExpiresAt = DateTimeOffset.Now.AddHours(5),
        };

        Assert.Contains("4 h", status.Summary());   // 4 h 59 min after the clock ticks on
    }

    [Fact]
    public void A_still_renewable_login_that_has_lapsed_says_so()
    {
        var status = new AuthStatus
        {
            Group = "Claude",
            IsValid = true,
            ExpiresAt = DateTimeOffset.Now.AddMinutes(-1),
        };

        Assert.Equal(Loc.T("auth.expiredRenew"), status.Summary());
    }
}
