using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ResultCapTests
{
    [Theory]
    [InlineData(8, 100, 8)]
    [InlineData(100, 100, 100)]
    [InlineData(108, 100, 108)]
    [InlineData(110, 100, 110)]
    [InlineData(111, 100, 100)]
    [InlineData(500, 100, 100)]
    [InlineData(52, 50, 52)]
    [InlineData(56, 50, 50)]
    [InlineData(2, 1, 1)]
    public void Shown_ReturnsTheWholeListOnlyWhenTheOverflowFitsTheSlack(int total, int cap, int expected) =>
        Assert.Equal(expected, ResultCap.Shown(total, cap));

    [Fact]
    public void Shown_WithANonPositiveCap_FallsBackToTheCap() =>
        Assert.Equal(0, ResultCap.Shown(42, 0));

    [Fact]
    public void Capped_ReturnsTheWholeCollectionWhenTheOverflowFitsTheSlack() =>
        Assert.Equal(11, Enumerable.Range(0, 11).ToArray().Capped(10).Count());


    [Fact]
    public void Capped_TruncatesToTheCapWhenTheOverflowExceedsTheSlack() =>
        Assert.Equal(10, Enumerable.Range(0, 40).ToArray().Capped(10).Count());
}
