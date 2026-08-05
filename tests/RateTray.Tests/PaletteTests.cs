using System.Drawing;
using RateTray.Configuration;
using RateTray.Ui;

namespace RateTray.Tests;

public class PaletteTests
{
    private static Palette Build(Action<AppConfig>? tweak = null)
    {
        var config = new AppConfig();
        tweak?.Invoke(config);
        return new Palette(config);
    }

    [Fact]
    public void Each_service_gets_its_own_colour()
    {
        var palette = Build();

        Assert.NotEqual(palette.Service("Claude"), palette.Service("Codex"));
    }

    [Fact]
    public void Unknown_group_falls_back_to_the_claude_colour()
    {
        var palette = Build();

        Assert.Equal(palette.Service("Claude"), palette.Service("something-else"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(74)]
    public void Below_the_warn_threshold_a_reading_keeps_its_service_colour(double percent)
    {
        var palette = Build();

        Assert.Equal(palette.Service("Codex"), palette.ForPercent("Codex", percent));
    }

    [Theory]
    [InlineData(75)]
    [InlineData(89)]
    public void From_the_warn_threshold_severity_wins_for_every_service(double percent)
    {
        var palette = Build();

        Assert.Equal(palette.Warn, palette.ForPercent("Claude", percent));
        Assert.Equal(palette.Warn, palette.ForPercent("Codex", percent));
    }

    [Theory]
    [InlineData(90)]
    [InlineData(100)]
    public void From_the_critical_threshold_the_critical_colour_is_used(double percent)
    {
        var palette = Build();

        Assert.Equal(palette.Critical, palette.ForPercent("Claude", percent));
    }

    [Fact]
    public void Severity_colours_are_derived_from_the_service_colours()
    {
        var warm = Build(c => { c.Colors.Claude = "#D97757"; c.Colors.Codex = "#10A37F"; }).Warn;
        var pale = Build(c => { c.Colors.Claude = "#8899AA"; c.Colors.Codex = "#99AABB"; }).Warn;

        // Same hue, but the tone follows the service colours — otherwise a re-tinted palette
        // would keep a fixed, clashing amber.
        Assert.NotEqual(warm, pale);
        AssertHue(48, warm);
        AssertHue(48, pale);
    }

    /// <summary>
    /// Colours round-trip through 8-bit RGB, so a hue comes back a degree or two off. Anything
    /// within that band is quantisation, not a wrong colour.
    /// </summary>
    private static void AssertHue(double expected, Color actual)
    {
        var hue = Harmony.ToHsl(actual).H;
        var delta = Math.Abs(((hue - expected + 540) % 360) - 180);

        Assert.True(delta <= 2, $"expected hue ≈{expected}, got {hue:0.0}");
    }

    [Fact]
    public void Explicit_hex_overrides_the_derived_colour()
    {
        var palette = Build(c => c.Colors.Warn = "#123456");

        Assert.Equal(Color.FromArgb(255, 0x12, 0x34, 0x56), palette.Warn);
    }

    [Fact]
    public void Warn_and_critical_hues_follow_the_configured_angles()
    {
        var palette = Build(c => { c.Colors.WarnHue = 200; c.Colors.CriticalHue = 300; });

        AssertHue(200, palette.Warn);
        AssertHue(300, palette.Critical);
    }

    [Fact]
    public void Unknown_colour_is_almost_neutral()
    {
        Assert.True(Harmony.ToHsl(Build().Unknown).S < 0.15);
    }

    [Theory]
    [InlineData("#D97757", 0xD9, 0x77, 0x57)]
    [InlineData("D97757", 0xD9, 0x77, 0x57)]
    [InlineData("#abc", 0xAA, 0xBB, 0xCC)]
    [InlineData("#FFD97757", 0xD9, 0x77, 0x57)]
    public void Hex_colours_are_parsed_in_every_supported_form(string value, int r, int g, int b)
    {
        var palette = Build(c => c.Colors.Claude = value);

        Assert.Equal(Color.FromArgb(255, r, g, b), palette.Service("Claude"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-colour")]
    [InlineData("#12345")]
    [InlineData("#GGHHII")]
    public void An_unparseable_colour_falls_back_instead_of_throwing(string value)
    {
        var palette = Build(c => c.Colors.Claude = value);

        Assert.Equal(Color.FromArgb(255, 217, 119, 87), palette.Service("Claude"));
    }

    [Fact]
    public void Thresholds_are_read_from_the_config()
    {
        var palette = Build(c => { c.Thresholds.Warn = 30; c.Thresholds.Critical = 40; });

        Assert.Equal(palette.Service("Claude"), palette.ForPercent("Claude", 29));
        Assert.Equal(palette.Warn, palette.ForPercent("Claude", 30));
        Assert.Equal(palette.Critical, palette.ForPercent("Claude", 40));
    }

    // ------------------------------------------------- shading within a service

    [Fact]
    public void The_first_limit_of_a_service_keeps_the_brand_colour_exactly()
    {
        var palette = Build();

        Assert.Equal(
            Harmony.Legible(palette.Service("Claude"), onDark: true),
            palette.ForReading("Claude", 10, variant: 0, variantCount: 3, onDark: true));
    }

    [Fact]
    public void Limits_of_the_same_service_are_shaded_apart()
    {
        var palette = Build();

        var shades = Enumerable.Range(0, 3)
            .Select(v => palette.ForReading("Claude", 10, v, 3, onDark: true))
            .ToList();

        Assert.Equal(3, shades.Distinct().Count());
    }

    [Fact]
    public void Shading_keeps_the_brand_hue()
    {
        var palette = Build();
        var expected = Harmony.ToHsl(palette.Service("Claude")).H;

        foreach (var variant in Enumerable.Range(0, 3))
            AssertHue(expected, palette.ForReading("Claude", 10, variant, 3, onDark: true));
    }

    [Fact]
    public void Shading_moves_away_from_the_surface_in_both_themes()
    {
        var palette = Build();

        var onDark = Enumerable.Range(0, 3).Select(v => Harmony.ToHsl(palette.ForReading("Claude", 10, v, 3, true)).L).ToList();
        var onLight = Enumerable.Range(0, 3).Select(v => Harmony.ToHsl(palette.ForReading("Claude", 10, v, 3, false)).L).ToList();

        // Contrast established by the legibility pass must never be given back.
        Assert.True(onDark[0] < onDark[1] && onDark[1] < onDark[2]);
        Assert.True(onLight[0] > onLight[1] && onLight[1] > onLight[2]);
    }

    [Theory]
    [InlineData(75)]
    [InlineData(90)]
    [InlineData(100)]
    public void A_warning_is_never_diluted_into_shades(double percent)
    {
        var palette = Build();

        // Above the threshold every limit of every service shares one colour, or "amber" would
        // stop meaning one specific thing.
        var shades = Enumerable.Range(0, 3)
            .Select(v => palette.ForReading("Claude", percent, v, 3, onDark: true))
            .Append(palette.ForReading("Codex", percent, 0, 1, onDark: true))
            .Distinct();

        Assert.Single(shades);
    }

    [Fact]
    public void A_service_with_a_single_limit_is_not_shaded()
    {
        var palette = Build();

        Assert.Equal(
            Harmony.Legible(palette.Service("Codex"), onDark: true),
            palette.ForReading("Codex", 10, variant: 0, variantCount: 1, onDark: true));
    }

    [Fact]
    public void Shading_can_be_turned_off()
    {
        var palette = Build(c => c.Colors.ShadeSpread = 0);

        var shades = Enumerable.Range(0, 3)
            .Select(v => palette.ForReading("Claude", 10, v, 3, onDark: true))
            .Distinct();

        Assert.Single(shades);
    }

    [Fact]
    public void An_out_of_range_variant_does_not_escape_the_spread()
    {
        var palette = Build();

        Assert.Equal(
            palette.ForReading("Claude", 10, variant: 2, variantCount: 3, onDark: true),
            palette.ForReading("Claude", 10, variant: 99, variantCount: 3, onDark: true));
    }

    [Fact]
    public void Track_stays_dark_on_dark_and_light_on_light()
    {
        var bar = Build().Service("Claude");

        Assert.True(Harmony.ToHsl(Palette.Track(bar, dark: true)).L < 0.35);
        Assert.True(Harmony.ToHsl(Palette.Track(bar, dark: false)).L > 0.75);
    }
}
