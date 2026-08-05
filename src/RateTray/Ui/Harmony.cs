namespace RateTray.Ui;

/// <summary>
/// HSL helpers used to derive the rest of the palette from the two service colours, the way
/// a colour-wheel tool does: hue carries the meaning, while saturation and lightness are kept
/// on a shared tone so nothing in the UI looks louder than the rest.
/// </summary>
public static class Harmony
{
    public readonly record struct Hsl(double H, double S, double L)
    {
        /// <summary>Hue in degrees, wrapped into 0..360.</summary>
        public Hsl WithHue(double hue) => this with { H = ((hue % 360) + 360) % 360 };

        public Hsl Scale(double saturation, double lightness) => this with
        {
            S = Math.Clamp(S * saturation, 0, 1),
            L = Math.Clamp(L * lightness, 0, 1),
        };
    }

    public static Hsl ToHsl(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2.0;

        if (Math.Abs(max - min) < 1e-9) return new Hsl(0, 0, l);

        var d = max - min;
        var s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

        double h;
        if (Math.Abs(max - r) < 1e-9) h = (g - b) / d + (g < b ? 6 : 0);
        else if (Math.Abs(max - g) < 1e-9) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;

        return new Hsl(h * 60, s, l);
    }

    public static Color ToColor(Hsl hsl)
    {
        var h = ((hsl.H % 360) + 360) % 360 / 360.0;
        var s = Math.Clamp(hsl.S, 0, 1);
        var l = Math.Clamp(hsl.L, 0, 1);

        if (s < 1e-9)
        {
            var grey = (int)Math.Round(l * 255);
            return Color.FromArgb(255, grey, grey, grey);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;

        return Color.FromArgb(255,
            Channel(p, q, h + 1.0 / 3.0),
            Channel(p, q, h),
            Channel(p, q, h - 1.0 / 3.0));
    }

    private static int Channel(double p, double q, double t)
    {
        t = t switch { < 0 => t + 1, > 1 => t - 1, _ => t };

        var value = t switch
        {
            < 1.0 / 6.0 => p + (q - p) * 6 * t,
            < 1.0 / 2.0 => q,
            < 2.0 / 3.0 => p + (q - p) * (2.0 / 3.0 - t) * 6,
            _ => p,
        };

        return (int)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }

    /// <summary>Midpoint tone of several colours: their mean saturation and lightness.</summary>
    public static Hsl SharedTone(params Color[] anchors)
    {
        if (anchors.Length == 0) return new Hsl(0, 0.5, 0.5);

        var parts = anchors.Select(ToHsl).ToArray();
        return new Hsl(parts[0].H, parts.Average(p => p.S), parts.Average(p => p.L));
    }

    /// <summary>
    /// Lifts or lowers lightness until the colour reads against the surface it sits on,
    /// leaving hue and saturation — and therefore the brand identity — untouched.
    /// </summary>
    public static Color Legible(Color color, bool onDark)
    {
        var hsl = ToHsl(color);

        var adjusted = onDark
            ? hsl with { L = Math.Max(hsl.L, 0.55) }
            : hsl with { L = Math.Min(hsl.L, 0.42) };

        return ToColor(adjusted);
    }
}
