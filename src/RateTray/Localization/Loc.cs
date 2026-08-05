using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace RateTray.Localization;

/// <summary>
/// String table backed by the embedded <c>Localization/*.json</c> files.
///
/// Plain JSON rather than .resx: translators can copy one file and edit it without a
/// tooling round trip, a new language shows up in the menu with no code change, and the
/// files are embedded in the assembly so a single-file publish carries them along
/// (satellite assemblies do not get bundled).
/// </summary>
public static class Loc
{
    private const string Prefix = "RateTray.Localization.";
    private const string Suffix = ".json";

    /// <summary>English is the fallback for any key a translation has not covered yet.</summary>
    private const string FallbackLanguage = "en";

    private static readonly Dictionary<string, string> Fallback = LoadTable(FallbackLanguage);
    private static Dictionary<string, string> _active = Fallback;

    /// <summary>Two-letter code of the table actually in use.</summary>
    public static string Current { get; private set; } = FallbackLanguage;

    /// <summary>Culture used for dates and numbers, so weekday names match the language.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(FallbackLanguage);

    /// <summary>Language codes discovered in the assembly, sorted, e.g. ["de", "en"].</summary>
    public static IReadOnlyList<string> Available { get; } = typeof(Loc).Assembly
        .GetManifestResourceNames()
        .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) &&
                       name.EndsWith(Suffix, StringComparison.Ordinal))
        .Select(name => name[Prefix.Length..^Suffix.Length])
        .OrderBy(code => code, StringComparer.Ordinal)
        .ToList();

    /// <summary>The language "auto" resolves to on this machine.</summary>
    public static string SystemLanguage
    {
        get
        {
            var code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return Available.Contains(code, StringComparer.OrdinalIgnoreCase) ? code : FallbackLanguage;
        }
    }

    /// <param name="language">Two-letter code, or "auto" to follow the Windows UI language.</param>
    public static void Use(string? language)
    {
        var code = string.IsNullOrWhiteSpace(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? SystemLanguage
            : language.Trim().ToLowerInvariant();

        if (!Available.Contains(code, StringComparer.OrdinalIgnoreCase)) code = FallbackLanguage;

        Current = code;
        _active = code == FallbackLanguage ? Fallback : LoadTable(code);

        try { Culture = CultureInfo.GetCultureInfo(code); }
        catch (CultureNotFoundException) { Culture = CultureInfo.InvariantCulture; }
    }

    /// <summary>Display name of a language, taken from its own table.</summary>
    public static string DisplayName(string code)
    {
        var table = code == Current ? _active : LoadTable(code);
        return table.GetValueOrDefault("language.name", code.ToUpperInvariant());
    }

    public static string T(string key) =>
        _active.GetValueOrDefault(key) ?? Fallback.GetValueOrDefault(key) ?? key;

    public static string T(string key, params object?[] args)
    {
        try { return string.Format(Culture, T(key), args); }
        catch (FormatException) { return T(key); }   // a translation with a broken placeholder must not crash the tray
    }

    /// <summary>Formats a timestamp with the pattern the active language declares.</summary>
    public static string DateTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString(T("format.dateTime"), Culture);

    private static Dictionary<string, string> LoadTable(string code)
    {
        try
        {
            using var stream = typeof(Loc).Assembly.GetManifestResourceStream(Prefix + code + Suffix);
            if (stream is null) return [];

            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
