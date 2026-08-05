using RateTray.Configuration;
using RateTray.Model;

namespace RateTray.Tests;

public class UsageCacheTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"tbm-cache-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    private static LimitReading Reading(string id, double percent) => new()
    {
        Id = id,
        Label = "Week",
        Group = "Claude",
        Percent = percent,
        ResetsAt = new DateTimeOffset(2026, 8, 11, 2, 59, 59, TimeSpan.Zero),
        Window = TimeSpan.FromDays(7),
        IsActive = true,
        Variant = 1,
        VariantCount = 3,
    };

    [Fact]
    public void Readings_survive_a_round_trip()
    {
        var entries = new Dictionary<string, CachedReadings>
        {
            ["Claude"] = new(DateTimeOffset.Now.AddMinutes(-5), [Reading("claude.weekly_all", 17)]),
        };

        UsageCache.Save(entries, _path);
        var loaded = UsageCache.Load(_path);

        var reading = Assert.Single(loaded["Claude"].Readings);
        Assert.Equal("claude.weekly_all", reading.Id);
        Assert.Equal(17, reading.Percent);
        Assert.Equal(TimeSpan.FromDays(7), reading.Window);
        Assert.Equal(new DateTimeOffset(2026, 8, 11, 2, 59, 59, TimeSpan.Zero), reading.ResetsAt);
        Assert.True(reading.IsActive);
    }

    [Fact]
    public void The_shade_variant_survives_so_colours_stay_stable_across_restarts()
    {
        var entries = new Dictionary<string, CachedReadings>
        {
            ["Claude"] = new(DateTimeOffset.Now, [Reading("claude.weekly_all", 17)]),
        };

        UsageCache.Save(entries, _path);

        var reading = UsageCache.Load(_path)["Claude"].Readings.Single();
        Assert.Equal(1, reading.Variant);
        Assert.Equal(3, reading.VariantCount);
    }

    [Fact]
    public void Several_providers_are_kept_apart()
    {
        var entries = new Dictionary<string, CachedReadings>
        {
            ["Claude"] = new(DateTimeOffset.Now, [Reading("claude.session", 8)]),
            ["Codex"] = new(DateTimeOffset.Now, [Reading("codex.primary", 6)]),
        };

        UsageCache.Save(entries, _path);
        var loaded = UsageCache.Load(_path);

        Assert.Equal(2, loaded.Count);
        Assert.Equal("codex.primary", loaded["Codex"].Readings.Single().Id);
    }

    [Fact]
    public void Stale_entries_are_dropped_rather_than_shown_as_current()
    {
        var entries = new Dictionary<string, CachedReadings>
        {
            ["Claude"] = new(DateTimeOffset.Now.AddDays(-3), [Reading("claude.weekly_all", 17)]),
            ["Codex"] = new(DateTimeOffset.Now.AddHours(-1), [Reading("codex.primary", 6)]),
        };

        UsageCache.Save(entries, _path);
        var loaded = UsageCache.Load(_path);

        // A three-day-old reset time is worse than no number at all.
        Assert.False(loaded.ContainsKey("Claude"));
        Assert.True(loaded.ContainsKey("Codex"));
    }

    [Fact]
    public void The_fetch_time_is_preserved_so_the_ui_can_show_how_old_the_data_is()
    {
        var fetched = DateTimeOffset.Now.AddMinutes(-42);
        UsageCache.Save(new Dictionary<string, CachedReadings>
        {
            ["Claude"] = new(fetched, [Reading("claude.session", 8)]),
        }, _path);

        Assert.Equal(fetched.ToUnixTimeSeconds(), UsageCache.Load(_path)["Claude"].FetchedAt.ToUnixTimeSeconds());
    }

    [Fact]
    public void A_missing_file_loads_as_empty()
    {
        Assert.Empty(UsageCache.Load(Path.Combine(Path.GetTempPath(), $"tbm-absent-{Guid.NewGuid():N}.json")));
    }

    [Fact]
    public void A_corrupt_file_loads_as_empty_instead_of_throwing()
    {
        File.WriteAllText(_path, "{ not json");

        Assert.Empty(UsageCache.Load(_path));
    }

    [Fact]
    public void A_null_entry_is_dropped_instead_of_taking_the_start_up_down()
    {
        // Valid JSON, semantically empty: it deserialises without complaint, and the
        // NullReferenceException that followed is not one of the failures Load may swallow.
        File.WriteAllText(_path, """{ "Claude": null }""");

        Assert.Empty(UsageCache.Load(_path));
    }

    [Fact]
    public void An_entry_without_readings_is_dropped()
    {
        var stamp = DateTimeOffset.Now.ToString("o");
        File.WriteAllText(_path, $$"""{ "Claude": { "fetchedAt": "{{stamp}}", "readings": null } }""");

        Assert.Empty(UsageCache.Load(_path));
    }

    [Fact]
    public void A_null_reading_does_not_travel_with_the_ones_that_are_real()
    {
        var stamp = DateTimeOffset.Now.ToString("o");
        File.WriteAllText(_path, $$"""
            { "Claude": { "fetchedAt": "{{stamp}}", "readings": [ null,
              { "id": "claude.session", "label": "Session", "group": "Claude", "percent": 8 } ] } }
            """);

        Assert.Equal("claude.session", Assert.Single(UsageCache.Load(_path)["Claude"].Readings).Id);
    }

    [Fact]
    public void Freshness_is_the_same_question_when_a_failed_poll_falls_back()
    {
        var stale = new CachedReadings(DateTimeOffset.Now.AddDays(-3), [Reading("claude.session", 8)]);

        // The two-day limit used to apply only on load, so an app left running for days with a
        // provider down kept restoring the same numbers no restart would have shown.
        Assert.False(UsageCache.IsFresh(stale, DateTimeOffset.Now));
        Assert.True(UsageCache.IsFresh(stale with { FetchedAt = DateTimeOffset.Now.AddHours(-5) }, DateTimeOffset.Now));
    }

    [Fact]
    public void Saving_to_an_unwritable_path_is_survivable()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tbm-{Guid.NewGuid():N}");
        var impossible = Path.Combine(directory, "file.json", "nested.json");

        // A cache that cannot be written is a missing optimisation, not a reason to fail.
        UsageCache.Save(new Dictionary<string, CachedReadings>
        {
            ["Claude"] = new(DateTimeOffset.Now, [Reading("claude.session", 8)]),
        }, impossible);
    }
}
