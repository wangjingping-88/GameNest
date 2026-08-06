using GameNest.Application;

namespace GameNest.Application.Tests;

public sealed class ApplicationVersionTests
{
    [Theory]
    [InlineData("v0.2.1", 0, 2, 1)]
    [InlineData("1.10.3+build.8", 1, 10, 3)]
    [InlineData("2.0.0-preview.1", 2, 0, 0)]
    public void TryParseStableAcceptsThreePartReleaseVersion(
        string value,
        int major,
        int minor,
        int patch)
    {
        var parsed = ApplicationVersion.TryParseStable(value, out var version);

        Assert.True(parsed);
        Assert.Equal(new Version(major, minor, patch), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1.2")]
    [InlineData("v1.2.3.4")]
    [InlineData("release-1.2.3")]
    public void TryParseStableRejectsInvalidVersion(string? value)
    {
        Assert.False(ApplicationVersion.TryParseStable(value, out _));
    }

    [Fact]
    public void VersionComparisonUsesNumericOrdering()
    {
        Assert.True(new Version(0, 10, 0) > new Version(0, 2, 9));
    }
}
