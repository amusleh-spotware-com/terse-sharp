using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("v0.15.2", 0, 15, 2, false)]
    [InlineData("0.15.2", 0, 15, 2, false)]
    [InlineData("  V1.0.0  ", 1, 0, 0, false)]
    [InlineData("0.16.0-alpha.0.1", 0, 16, 0, true)]
    [InlineData("0.15.2+build.9", 0, 15, 2, false)]
    [InlineData("2.7", 2, 7, 0, false)]
    public void TryParse_ReadsTheCoreComponentsAndThePrereleaseMarker(string text, int major, int minor, int patch, bool prerelease)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        Assert.Equal(new ReleaseVersion(major, minor, patch, prerelease), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("main")]
    [InlineData("v")]
    [InlineData("0")]
    [InlineData("0.x.2")]
    [InlineData("-1.0.0")]
    public void TryParse_RefusesWhatIsNotAVersion(string text) =>
        Assert.False(ReleaseVersion.TryParse(text, out _));

    [Fact]
    public void IsNewerThan_ComparesMajorThenMinorThenPatch()
    {
        Assert.True(Parsed("0.16.0").IsNewerThan(Parsed("0.15.2")));
        Assert.True(Parsed("1.0.0").IsNewerThan(Parsed("0.99.99")));
        Assert.True(Parsed("0.15.3").IsNewerThan(Parsed("0.15.2")));
        Assert.False(Parsed("0.15.2").IsNewerThan(Parsed("0.15.2")));
        Assert.False(Parsed("0.15.1").IsNewerThan(Parsed("0.15.2")));
    }

    [Fact]
    public void IsNewerThan_RanksAReleaseAboveThePrereleaseOfTheSameVersion()
    {
        Assert.True(Parsed("0.16.0").IsNewerThan(Parsed("0.16.0-alpha.0.1")));
        Assert.False(Parsed("0.16.0-alpha.0.1").IsNewerThan(Parsed("0.16.0")));
        Assert.False(Parsed("0.15.3-alpha.0.4").IsNewerThan(Parsed("0.15.3-alpha.0.9")));
    }

    [Fact]
    public void IsNewerThan_SaysAReleaseIsNotNewerThanTheNextPrerelease() =>
        Assert.False(Parsed("0.15.2").IsNewerThan(Parsed("0.15.3-alpha.0.1")));

    [Fact]
    public void ToString_RendersTheCoreAndMarksAPrerelease()
    {
        Assert.Equal("0.15.2", Parsed("v0.15.2").ToString());
        Assert.Equal("0.16.0-pre", Parsed("0.16.0-alpha.0.1").ToString());
    }

    private static ReleaseVersion Parsed(string text) =>
        ReleaseVersion.TryParse(text, out var version) ? version : throw new InvalidOperationException(text);
}
