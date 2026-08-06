namespace RateTray.Tests;

/// <summary>Turning a GitHub tag name into a version the update check can compare.</summary>
public class UpdateCheckTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("V0.2.0", 0, 2, 0)]
    [InlineData("v1.2", 1, 2, 0)]                        // a two-part tag floors to build 0
    public void Parses_a_version_tag(string tag, int major, int minor, int build)
    {
        Assert.True(UpdateCheck.TryParseTag(tag, out var version));
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("nightly")]
    public void Rejects_anything_that_is_not_a_version(string? tag)
    {
        Assert.False(UpdateCheck.TryParseTag(tag, out _));
    }

    [Fact]
    public void A_newer_tag_compares_greater_than_an_older_one()
    {
        Assert.True(UpdateCheck.TryParseTag("v0.3.0", out var newer));
        Assert.True(UpdateCheck.TryParseTag("v0.2.0", out var older));
        Assert.True(newer > older);
    }
}
