using RateTray.Configuration;

namespace RateTray.Tests;

/// <summary>
/// settings.json is meant to be edited by hand — that is why it is plain JSON in a documented
/// place. Syntactically valid nonsense therefore has to arrive as a bad setting, not as a
/// start-up that dies before the first icon is drawn: a null sub-object or an absurd number
/// deserialises without complaint and used to reach the UI untouched.
/// </summary>
public class ConfigStoreTests
{
    [Fact]
    public void A_null_icon_list_loads_as_an_empty_one()
    {
        // This one broke the icons menu inside the constructor, before anything was visible.
        Assert.Empty(ConfigStore.FromJson("""{ "icons": null }""").Icons);
    }

    [Fact]
    public void Blank_icon_ids_are_dropped_rather_than_compared_against_later()
    {
        var config = ConfigStore.FromJson("""{ "icons": ["claude.session", null, "  ", " codex.primary "] }""");

        Assert.Equal(new[] { "claude.session", "codex.primary" }, config.Icons);
    }

    [Fact]
    public void A_null_theme_never_reaches_the_taskbar_check()
    {
        var config = ConfigStore.FromJson("""{ "theme": null, "language": null, "fontFamily": "  " }""");

        Assert.Equal("auto", config.Theme);
        Assert.Equal("auto", config.Language);
        Assert.Equal("Segoe UI", config.FontFamily);
    }

    [Fact]
    public void Null_sub_objects_become_defaults_instead_of_null_references()
    {
        var config = ConfigStore.FromJson(
            """{ "colors": null, "thresholds": null, "notifications": null, "claude": null, "codex": null }""");

        Assert.Equal("#D97757", config.Colors.Claude);
        Assert.Equal(75, config.Thresholds.Warn);
        Assert.True(config.Notifications.Enabled);
        Assert.Equal("https://api.anthropic.com/api/oauth/usage", config.Claude.UsageUrl);
        Assert.True(config.Codex.Enabled);
    }

    [Fact]
    public void A_refresh_interval_that_would_overflow_the_poll_timer_is_clamped()
    {
        var config = ConfigStore.FromJson("""{ "refreshSeconds": 3000000 }""");

        // 3_000_000 * 1000 does not fit an int, and the timer was handed the negative result.
        Assert.Equal(86_400, config.RefreshSeconds);
        Assert.True((long)config.RefreshSeconds * 1000 <= int.MaxValue);
    }

    [Fact]
    public void A_refresh_interval_below_the_floor_is_raised_to_it()
    {
        // The usage endpoint rate-limits; a one-second poll would eventually earn a 429.
        Assert.Equal(30, ConfigStore.FromJson("""{ "refreshSeconds": 1 }""").RefreshSeconds);
    }

    [Fact]
    public void Percentages_and_hues_are_clamped_to_their_ranges()
    {
        var config = ConfigStore.FromJson(
            """{ "thresholds": { "warn": -5, "critical": 900 }, "notifications": { "atPercent": 400 }, "colors": { "warnHue": 400, "criticalHue": -20, "shadeSpread": 9 } }""");

        Assert.Equal(0, config.Thresholds.Warn);
        Assert.Equal(100, config.Thresholds.Critical);
        Assert.Equal(100, config.Notifications.AtPercent);
        Assert.Equal(359, config.Colors.WarnHue);
        Assert.Equal(0, config.Colors.CriticalHue);
        Assert.Equal(1.0, config.Colors.ShadeSpread);
    }

    [Fact]
    public void Thresholds_are_clamped_but_not_reordered()
    {
        // Warn above critical only means every value turns red a step earlier. It reads
        // strangely, but someone may have meant it — silently swapping them would not.
        var config = ConfigStore.FromJson("""{ "thresholds": { "warn": 90, "critical": 60 } }""");

        Assert.Equal(90, config.Thresholds.Warn);
        Assert.Equal(60, config.Thresholds.Critical);
    }

    [Fact]
    public void Blank_urls_fall_back_to_the_endpoints_that_exist()
    {
        var config = ConfigStore.FromJson("""{ "claude": { "usageUrl": "", "tokenUrl": null, "clientId": "  " } }""");

        Assert.Equal("https://api.anthropic.com/api/oauth/usage", config.Claude.UsageUrl);
        Assert.StartsWith("https://console.anthropic.com/", config.Claude.TokenUrl);
        Assert.NotEmpty(config.Claude.ClientId);
    }

    [Fact]
    public void A_blank_path_collapses_to_null_so_the_default_location_applies()
    {
        var config = ConfigStore.FromJson(
            """{ "claude": { "credentialsPath": "   " }, "codex": { "executablePath": "" } }""");

        Assert.Null(config.Claude.CredentialsPath);
        Assert.Null(config.Codex.ExecutablePath);
    }

    [Fact]
    public void A_timeout_stays_long_enough_to_be_worth_attempting()
    {
        var config = ConfigStore.FromJson("""{ "claude": { "timeoutSeconds": 0 }, "codex": { "timeoutSeconds": -1 } }""");

        Assert.Equal(5, config.Claude.TimeoutSeconds);
        Assert.Equal(5, config.Codex.TimeoutSeconds);
    }

    [Fact]
    public void An_empty_object_keeps_every_default()
    {
        var config = ConfigStore.FromJson("{}");
        var defaults = new AppConfig();

        Assert.Equal(defaults.RefreshSeconds, config.RefreshSeconds);
        Assert.Equal(defaults.Theme, config.Theme);
        Assert.Equal(defaults.MaxBackoffMinutes, config.MaxBackoffMinutes);
        Assert.Equal(defaults.Colors.ShadeSpread, config.Colors.ShadeSpread);
    }

    [Fact]
    public void Settings_that_were_actually_chosen_are_left_alone()
    {
        // Normalisation repairs what cannot work; it does not have opinions about the rest.
        var config = ConfigStore.FromJson(
            """{ "refreshSeconds": 120, "theme": "dark", "icons": ["claude.session"], "colors": { "shadeSpread": 0.3, "warn": "#FFAA00" } }""");

        Assert.Equal(120, config.RefreshSeconds);
        Assert.Equal("dark", config.Theme);
        Assert.Equal("claude.session", Assert.Single(config.Icons));
        Assert.Equal(0.3, config.Colors.ShadeSpread);
        Assert.Equal("#FFAA00", config.Colors.Warn);
    }

    [Fact]
    public void A_derived_colour_stays_null_rather_than_becoming_blank()
    {
        // Null means "derive it from the service colours", which is not the same as "".
        var config = ConfigStore.FromJson("""{ "colors": { "warn": "  ", "critical": null } }""");

        Assert.Null(config.Colors.Warn);
        Assert.Null(config.Colors.Critical);
    }
}
