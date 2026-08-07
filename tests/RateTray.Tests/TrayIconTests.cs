using RateTray.Ui;

namespace RateTray.Tests;

/// <summary>The per-icon GUID that gives each tray icon its own persistent Windows identity.</summary>
public class TrayIconTests
{
    [Fact]
    public void The_same_id_always_maps_to_the_same_guid()
    {
        // Stability across runs is the whole point — Windows keys the settings entry on it.
        Assert.Equal(TrayIcon.GuidFor("claude.session"), TrayIcon.GuidFor("claude.session"));
    }

    [Fact]
    public void Different_ids_map_to_different_guids()
    {
        Assert.NotEqual(TrayIcon.GuidFor("claude.session"), TrayIcon.GuidFor("codex.primary"));
    }

    [Fact]
    public void The_guid_is_never_empty()
    {
        Assert.NotEqual(Guid.Empty, TrayIcon.GuidFor("claude.weekly_scoped.fable"));
    }
}
