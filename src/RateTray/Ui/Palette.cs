using RateTray.Configuration;

namespace RateTray.Ui;

/// <summary>
/// Single source of colour for tray icons, hover cards and the details window, so a value has
/// the same colour wherever it appears.
///
/// Below the warn threshold a reading is drawn in its service colour — that is what tells the
/// tray icons apart. From the warn threshold on, severity wins for every service: a warning
/// has to be recognisable without knowing which colour belongs to which service.
///
/// The severity and neutral colours are not hardcoded but derived from the two service
/// colours: only the hue is set (amber, crimson, grey), while saturation and lightness are
/// taken from the shared tone of the service colours. That keeps the palette harmonious when
/// someone swaps in their own brand colours, instead of leaving a fixed red clashing with a
/// re-tinted set. Setting any colour explicitly in settings.json overrides its derivation.
/// </summary>
public sealed class Palette(AppConfig config)
{
    private static readonly Color ClaudeFallback = Color.FromArgb(217, 119, 87);
    private static readonly Color CodexFallback = Color.FromArgb(16, 163, 127);

    public Color Service(string group) => group.Equals("Codex", StringComparison.OrdinalIgnoreCase)
        ? Parse(config.Colors.Codex, CodexFallback)
        : Parse(config.Colors.Claude, ClaudeFallback);

    /// <summary>Mean saturation and lightness of the service colours.</summary>
    private Harmony.Hsl Tone => Harmony.SharedTone(Service("Claude"), Service("Codex"));

    /// <summary>Amber. Slightly more saturated than the tone so it reads as a signal.</summary>
    public Color Warn => Derived(config.Colors.Warn, config.Colors.WarnHue, 1.18, 1.04);

    /// <summary>Crimson. Deeper and more saturated, which separates it from a warm service hue.</summary>
    public Color Critical => Derived(config.Colors.Critical, config.Colors.CriticalHue, 1.35, 0.90);

    /// <summary>Near-neutral grey of the same family, used when a limit has no value.</summary>
    public Color Unknown => config.Colors.Unknown is { Length: > 0 } fixedColor
        ? Parse(fixedColor, Color.FromArgb(140, 145, 155))
        : Harmony.ToColor(Tone.WithHue(config.Colors.WarnHue).Scale(0.10, 1.02));

    public Color ForPercent(string group, double percent)
    {
        if (percent >= config.Thresholds.Critical) return Critical;
        if (percent >= config.Thresholds.Warn) return Warn;
        return Service(group);
    }

    /// <summary>Same as <see cref="ForPercent"/>, adjusted to stay readable on the surface.</summary>
    public Color ForPercent(string group, double percent, bool onDark) =>
        Harmony.Legible(ForPercent(group, percent), onDark);

    /// <summary>
    /// Colour for one reading, shaded apart from the other limits of the same service so that
    /// three Claude icons in a row are distinguishable by more than their position.
    ///
    /// Shading applies only below the warning threshold. Once a limit is amber or red that
    /// meaning must not be diluted into a set of near-identical tints, so every service and
    /// every limit shares exactly one warning colour and one critical colour.
    /// </summary>
    public Color ForReading(string group, double percent, int variant, int variantCount, bool onDark)
    {
        if (percent >= config.Thresholds.Warn) return ForPercent(group, percent, onDark);

        return Shade(Harmony.Legible(Service(group), onDark), variant, variantCount, onDark);
    }

    /// <summary>
    /// Steps lightness away from the service colour, leaving hue and saturation alone. Variant
    /// 0 keeps the brand colour exactly; later ones move towards the surface's opposite so the
    /// contrast that <see cref="Harmony.Legible"/> established is never given back.
    /// </summary>
    private Color Shade(Color baseColor, int variant, int variantCount, bool onDark)
    {
        var spread = config.Colors.ShadeSpread;
        if (variantCount <= 1 || spread <= 0 || variant <= 0) return baseColor;

        var hsl = Harmony.ToHsl(baseColor);
        var step = Math.Clamp(variant, 0, variantCount - 1) / (double)(variantCount - 1) * spread;

        var lightness = onDark
            ? Math.Min(hsl.L + step, 0.88)
            : Math.Max(hsl.L - step, 0.16);

        // Chroma drops off as a colour approaches white or black, so saturation is nudged up
        // to compensate. Without this the last shade of a set washes out to a pale tint and
        // stops reading as the service's colour at all.
        var saturation = Math.Clamp(hsl.S * (1 + step * 0.9), 0, 1);

        return Harmony.ToColor(hsl with { L = lightness, S = saturation });
    }

    /// <summary>Track behind a progress bar: the bar's own hue, drained down to a surface tint.</summary>
    public static Color Track(Color bar, bool dark)
    {
        var hsl = Harmony.ToHsl(bar);
        return Harmony.ToColor(hsl with { S = hsl.S * 0.28, L = dark ? 0.19 : 0.88 });
    }

    private Color Derived(string? configured, int hue, double saturation, double lightness) =>
        configured is { Length: > 0 }
            ? Parse(configured, Harmony.ToColor(Tone.WithHue(hue).Scale(saturation, lightness)))
            : Harmony.ToColor(Tone.WithHue(hue).Scale(saturation, lightness));

    /// <summary>Accepts #RGB, #RRGGBB and #AARRGGBB; anything unparseable falls back.</summary>
    private static Color Parse(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        var text = value.Trim().TrimStart('#');
        try
        {
            return text.Length switch
            {
                3 => Color.FromArgb(255,
                    Convert.ToInt32($"{text[0]}{text[0]}", 16),
                    Convert.ToInt32($"{text[1]}{text[1]}", 16),
                    Convert.ToInt32($"{text[2]}{text[2]}", 16)),
                6 => Color.FromArgb(unchecked((int)(0xFF000000 | Convert.ToUInt32(text, 16)))),
                8 => Color.FromArgb(unchecked((int)Convert.ToUInt32(text, 16))),
                _ => fallback,
            };
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
