using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ExploreSteerTests
{
    [Fact]
    public void Note_OnTheSecondDistinctNavigationTool_SteersToExploreSymbol()
    {
        ExploreSteer.Forget();

        Assert.Null(ExploreSteer.Note("get_symbol_source", "OrderService.Submit"));

        var note = ExploreSteer.Note("find_usages", "OrderService.Submit");

        Assert.NotNull(note);
        Assert.Contains("explore_symbol symbolId=\"OrderService.Submit\"", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Note_SteersOncePerSymbol()
    {
        ExploreSteer.Forget();
        ExploreSteer.Note("get_type_outline", "OrderRouter.Route");

        Assert.NotNull(ExploreSteer.Note("find_usages", "OrderRouter.Route"));
        Assert.Null(ExploreSteer.Note("find_usages", "OrderRouter.Route"));
    }

    [Fact]
    public void Note_ForOneToolAskedTwice_SaysNothing()
    {
        ExploreSteer.Forget();

        Assert.Null(ExploreSteer.Note("find_usages", "OrderBook.Add"));
        Assert.Null(ExploreSteer.Note("find_usages", "OrderBook.Add"));
    }

    [Fact]
    public void Note_AfterExploreSymbolAnsweredTheId_SaysNothing()
    {
        ExploreSteer.Forget();
        ExploreSteer.Note("explore_symbol", "Fill.Price");
        ExploreSteer.Note("get_symbol_source", "Fill.Price");

        Assert.Null(ExploreSteer.Note("find_usages", "Fill.Price"));
    }

    [Fact]
    public void Note_OnAToolThatIsNotNavigation_SaysNothing()
    {
        ExploreSteer.Forget();

        Assert.Null(ExploreSteer.Note("read_text", "OrderService"));
        Assert.Null(ExploreSteer.Note("build", "OrderService"));
    }
}
