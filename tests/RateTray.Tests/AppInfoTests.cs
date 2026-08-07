namespace RateTray.Tests;

/// <summary>Version handling behind the About dialog's update check.</summary>
public class AppInfoTests
{
    [Fact]
    public void Normalize_makes_a_tag_and_the_assembly_version_compare_equal()
    {
        // A tag parses to "0.2.0" (Build 0, Revision -1); the assembly version is "0.2.0.0". Without
        // normalising, Version treats those as different and the check would falsely cry "update".
        Assert.Equal(AppInfo.Normalize(new Version(0, 2, 0)), AppInfo.Normalize(new Version(0, 2, 0, 0)));
    }

    [Fact]
    public void Normalize_keeps_major_minor_build_and_drops_the_revision()
    {
        Assert.Equal(new Version(1, 4, 2), AppInfo.Normalize(new Version(1, 4, 2, 99)));
    }

    [Fact]
    public void Normalize_floors_a_two_part_version_to_build_zero()
    {
        Assert.Equal(new Version(1, 5, 0), AppInfo.Normalize(new Version(1, 5)));
    }

    [Fact]
    public void The_running_version_reads_as_a_sensible_semver()
    {
        Assert.True(AppInfo.SemVer >= new Version(0, 1, 0));
        Assert.Equal(-1, AppInfo.SemVer.Revision);      // normalised — no fourth component
    }
}
