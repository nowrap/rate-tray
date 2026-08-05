using System.Drawing;
using RateTray.Ui;

namespace RateTray.Tests;

public class HarmonyTests
{
    [Theory]
    [InlineData(0xD9, 0x77, 0x57)]
    [InlineData(0x10, 0xA3, 0x7F)]
    [InlineData(0x00, 0x00, 0x00)]
    [InlineData(0xFF, 0xFF, 0xFF)]
    [InlineData(0x80, 0x80, 0x80)]
    public void Hsl_conversion_round_trips(int r, int g, int b)
    {
        var original = Color.FromArgb(255, r, g, b);

        var result = Harmony.ToColor(Harmony.ToHsl(original));

        // One unit of drift per channel is rounding, not a conversion error.
        Assert.InRange(Math.Abs(result.R - original.R), 0, 1);
        Assert.InRange(Math.Abs(result.G - original.G), 0, 1);
        Assert.InRange(Math.Abs(result.B - original.B), 0, 1);
    }

    [Fact]
    public void Grey_has_no_saturation()
    {
        Assert.Equal(0, Harmony.ToHsl(Color.FromArgb(255, 128, 128, 128)).S, 3);
    }

    [Theory]
    [InlineData(400, 40)]
    [InlineData(-20, 340)]
    [InlineData(360, 0)]
    public void Hue_wraps_into_the_colour_wheel(double input, double expected)
    {
        var hsl = new Harmony.Hsl(0, 0.5, 0.5).WithHue(input);

        Assert.Equal(expected, hsl.H, 3);
    }

    [Fact]
    public void Scaling_clamps_saturation_and_lightness_into_range()
    {
        var hsl = new Harmony.Hsl(10, 0.9, 0.9).Scale(5, 5);

        Assert.Equal(1, hsl.S, 3);
        Assert.Equal(1, hsl.L, 3);
    }

    [Fact]
    public void Shared_tone_averages_saturation_and_lightness()
    {
        var tone = Harmony.SharedTone(
            Harmony.ToColor(new Harmony.Hsl(0, 0.2, 0.4)),
            Harmony.ToColor(new Harmony.Hsl(180, 0.8, 0.6)));

        Assert.Equal(0.5, tone.S, 1);
        Assert.Equal(0.5, tone.L, 1);
    }

    [Fact]
    public void Legible_lifts_a_dark_colour_on_a_dark_surface()
    {
        var dark = Color.FromArgb(255, 16, 60, 45);

        var adjusted = Harmony.Legible(dark, onDark: true);

        Assert.True(Harmony.ToHsl(adjusted).L >= 0.55);
    }

    [Fact]
    public void Legible_darkens_a_light_colour_on_a_light_surface()
    {
        var light = Color.FromArgb(255, 250, 230, 210);

        var adjusted = Harmony.Legible(light, onDark: false);

        Assert.True(Harmony.ToHsl(adjusted).L <= 0.42);
    }

    [Fact]
    public void Legible_preserves_hue_so_brand_identity_survives()
    {
        var brand = Color.FromArgb(255, 0x10, 0xA3, 0x7F);

        var adjusted = Harmony.Legible(brand, onDark: true);

        Assert.Equal(
            Math.Round(Harmony.ToHsl(brand).H),
            Math.Round(Harmony.ToHsl(adjusted).H));
    }
}
