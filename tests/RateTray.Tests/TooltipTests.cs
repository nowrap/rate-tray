using RateTray.Localization;
using RateTray.Model;

namespace RateTray.Tests;

public class TooltipTests
{
    public TooltipTests() => Loc.Use("en");

    private static LimitReading Reading(string label, double percent, DateTimeOffset? reset) => new()
    {
        Id = "claude.weekly_all",
        Label = label,
        Group = "Claude",
        Percent = percent,
        ResetsAt = reset,
    };

    [Fact]
    public void A_tooltip_never_exceeds_the_winforms_limit()
    {
        // NotifyIcon.Text throws above 63 characters, so this is a hard cap, not a guideline.
        var reading = Reading(new string('x', 200), 100, DateTimeOffset.Now.AddDays(6));

        var tooltip = TrayApp.Tooltip("claude.weekly_all", reading, null);

        Assert.True(tooltip.Length <= 63, $"was {tooltip.Length} characters");
    }

    [Fact]
    public void A_realistic_tooltip_fits_without_being_truncated()
    {
        var reading = Reading("Week · All models", 17, DateTimeOffset.Now.AddDays(5).AddHours(17));

        var tooltip = TrayApp.Tooltip("claude.weekly_all", reading, null);

        Assert.DoesNotContain('…', tooltip);
        Assert.Contains("17 %", tooltip);
    }

    [Fact]
    public void A_tooltip_without_a_reset_shows_only_the_value()
    {
        var tooltip = TrayApp.Tooltip("claude.weekly_all", Reading("Week", 17, null), null);

        Assert.Equal("Week: 17 %", tooltip);
    }

    [Fact]
    public void A_missing_reading_falls_back_to_the_provider_error()
    {
        var tooltip = TrayApp.Tooltip("codex.primary", null, "codex not found");

        Assert.Contains("codex.primary", tooltip);
        Assert.Contains("codex not found", tooltip);
    }

    [Theory]
    [InlineData("short", "short")]
    public void Clamp_leaves_short_text_untouched(string input, string expected)
    {
        Assert.Equal(expected, TrayApp.Clamp(input));
    }

    [Fact]
    public void Clamp_marks_truncated_text_with_an_ellipsis()
    {
        var clamped = TrayApp.Clamp(new string('a', 100));

        Assert.Equal(63, clamped.Length);
        Assert.EndsWith("…", clamped);
    }

    [Theory]
    [InlineData("codex.primary", "Codex")]
    [InlineData("codex.secondary", "Codex")]
    [InlineData("codex.codex_max.primary", "Codex")]
    [InlineData("claude.session", "Claude")]
    [InlineData("claude.weekly_scoped.fable", "Claude")]
    public void An_id_maps_back_to_its_service(string id, string expected)
    {
        Assert.Equal(expected, TrayApp.GroupOf(id));
    }
}
