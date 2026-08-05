using System.Text.Json;
using System.Text.RegularExpressions;
using RateTray.Localization;

namespace RateTray.Tests;

public class LocalizationTests
{
    private const string Prefix = "RateTray.Localization.";

    private static Dictionary<string, string> Table(string code)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream(Prefix + code + ".json")
                           ?? throw new InvalidOperationException($"{code}.json is not embedded");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    [Fact]
    public void Both_shipped_languages_are_discovered()
    {
        Assert.Contains("en", Loc.Available);
        Assert.Contains("de", Loc.Available);
    }

    [Fact]
    public void Every_language_defines_exactly_the_english_keys()
    {
        var english = Table("en").Keys.ToHashSet();

        foreach (var code in Loc.Available.Where(c => c != "en"))
        {
            var keys = Table(code).Keys.ToHashSet();

            Assert.Empty(english.Except(keys));   // nothing missing
            Assert.Empty(keys.Except(english));   // nothing left over from a renamed key
        }
    }

    [Fact]
    public void Every_translation_uses_the_same_placeholders_as_english()
    {
        var english = Table("en");

        foreach (var code in Loc.Available.Where(c => c != "en"))
        {
            var translated = Table(code);

            foreach (var (key, value) in english)
            {
                // A translation that drops {1} would silently lose data at runtime; one that
                // invents {2} would throw a FormatException.
                Assert.Equal(Placeholders(value), Placeholders(translated[key]));
            }
        }
    }

    private static SortedSet<string> Placeholders(string value) =>
        [.. Regex.Matches(value, @"\{\d+\}").Select(m => m.Value)];

    [Fact]
    public void Unknown_language_falls_back_to_english()
    {
        Loc.Use("xx");

        Assert.Equal("en", Loc.Current);
    }

    [Fact]
    public void Auto_resolves_to_a_language_that_exists()
    {
        Loc.Use("auto");

        Assert.Contains(Loc.Current, Loc.Available);
    }

    [Fact]
    public void Missing_key_returns_the_key_itself_rather_than_throwing()
    {
        Loc.Use("de");

        Assert.Equal("no.such.key", Loc.T("no.such.key"));
    }

    [Fact]
    public void Translation_is_applied_and_formatted()
    {
        Loc.Use("de");
        Assert.Equal("Beenden", Loc.T("menu.quit"));

        Loc.Use("en");
        Assert.Equal("Quit", Loc.T("menu.quit"));
        Assert.Equal("Warn at 80 %", Loc.T("menu.notifyAt", 80));
    }

    [Fact]
    public void Display_name_of_a_language_comes_from_its_own_table()
    {
        Loc.Use("en");

        Assert.Equal("Deutsch", Loc.DisplayName("de"));
        Assert.Equal("English", Loc.DisplayName("en"));
    }

    [Fact]
    public void Date_format_follows_the_active_language()
    {
        var stamp = new DateTimeOffset(2026, 8, 11, 4, 59, 0, TimeSpan.Zero).ToLocalTime();

        Loc.Use("de");
        var german = Loc.DateTime(stamp);

        Loc.Use("en");
        var english = Loc.DateTime(stamp);

        Assert.NotEqual(german, english);
        Assert.Contains("11", german);
    }
}
