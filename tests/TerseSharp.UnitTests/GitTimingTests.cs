using TerseSharp.Server.Tools;

namespace TerseSharp.UnitTests;

public sealed class GitTimingTests
{
    private const string Listing = "1 files\na.md  +1 -0  M";

    [Fact]
    public void Appended_OverTheThreshold_AddsOneLineNamingEveryPhaseInWholeMilliseconds()
    {
        var timing = new GitTools.GitTiming(TimeSpan.FromMilliseconds(1200), TimeSpan.FromMilliseconds(3000.7), TimeSpan.FromMilliseconds(4500), TimeSpan.FromMilliseconds(2400));

        var text = timing.Appended(Listing, TimeSpan.FromSeconds(10));

        Assert.Equal(Listing + "\ntiming stamp=1200 numstat=3000 name-status=4500 ls-files=2400", text);
    }

    [Fact]
    public void Appended_AtExactlyTheThreshold_ReturnsTheResponseUntouched()
    {
        var timing = new GitTools.GitTiming(TimeSpan.Zero, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2));

        Assert.Same(Listing, timing.Appended(Listing, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Appended_ForTheDefaultTimingAFailedListingCarries_NeverAddsALineEvenAtAZeroThreshold() =>
        Assert.Same(Listing, default(GitTools.GitTiming).Appended(Listing, TimeSpan.Zero));
}
