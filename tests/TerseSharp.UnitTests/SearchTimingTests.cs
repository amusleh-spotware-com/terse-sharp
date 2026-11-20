using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class SearchTimingTests
{
    private const string Listing = "1 matches\na.md:1  alpha";

    [Fact]
    public void Appended_OverTheThreshold_AddsOneLineNamingTheIndexAndScanPhasesInWholeMilliseconds()
    {
        var timing = new SearchTiming(TimeSpan.FromMilliseconds(4200.9), TimeSpan.FromMilliseconds(118000), 31800);

        Assert.Equal(Listing + "\ntiming index=4200 scan=118000 files=31800", timing.Appended(Listing, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Appended_AtExactlyTheThreshold_ReturnsTheSearchAnswerUntouched() =>
        Assert.Same(Listing, new SearchTiming(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), 10).Appended(Listing, TimeSpan.FromSeconds(10)));

    [Fact]
    public void Appended_ForTheDefaultSearchTiming_NeverAddsALineEvenAtAZeroThreshold() =>
        Assert.Same(Listing, default(SearchTiming).Appended(Listing, TimeSpan.Zero));

    [Theory]
    [InlineData("0", 0)]
    [InlineData("250", 250)]
    [InlineData(null, 10_000)]
    [InlineData("-1", 10_000)]
    [InlineData("soon", 10_000)]
    public void Configured_ParsesWholeMillisecondsAndFallsBackToTenSeconds(string? milliseconds, int expected) =>
        Assert.Equal(TimeSpan.FromMilliseconds(expected), SlowCall.Configured(milliseconds));
}
