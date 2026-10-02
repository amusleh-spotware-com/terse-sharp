using TerseSharp.Core;

namespace TerseSharp.UnitTests;

[Collection(nameof(OfferMemoCollection))]
public sealed class OfferMemoTests
{
    [Fact]
    public void Offer_WhenRemembered_IsPrintedOnTheFirstResponseOfEachToolAndOmittedFromItsNext() => Remembered(() =>
    {
        Assert.Equal("1 matches\na.cs:1  x\ncontainers=true names it", Offered("search_text"));
        Assert.Equal("1 matches\na.cs:1  x", Offered("search_text"));
        Assert.Equal("1 matches\na.cs:1  x\ncontainers=true names it", Offered("search_regex"));
    });

    [Fact]
    public void Offer_WhenNotRemembered_IsPrintedOnEveryResponse()
    {
        OfferMemo.Forget();

        Assert.EndsWith("containers=true names it", Offered("search_text"), StringComparison.Ordinal);
        Assert.EndsWith("containers=true names it", Offered("search_text"), StringComparison.Ordinal);
    }

    [Fact]
    public void Offer_UnderVerbose_IsAlwaysPrintedAndDoesNotSpendTheFirstShowing() => Remembered(() =>
    {
        var verbose = new ResponseBuilder("read_text", "a.cs").Verbose(true).Summary(2, 2, "lines").Offer("condensed", "condensed=true").ToString();

        Assert.EndsWith("condensed=true", verbose, StringComparison.Ordinal);
        Assert.EndsWith("condensed=true", Condensed(), StringComparison.Ordinal);
        Assert.DoesNotContain("condensed=true", Condensed(), StringComparison.Ordinal);
    });

    [Fact]
    public void Summary_OfACompleteListing_AdvertisesItsNarrowingParameterOncePerTool() => Remembered(() =>
    {
        Assert.Equal("30 projects - narrow with filter=", Listing(30, 30));
        Assert.Equal("30 projects", Listing(30, 30));
    });

    [Fact]
    public void Summary_WhenTruncated_StillNamesItsNarrowingParameterOnEveryResponse() => Remembered(() =>
    {
        Assert.Equal("30 projects - narrow with filter=", Listing(30, 30));
        Assert.Equal("2/9 projects truncated - 7 NOT shown - narrow with filter=", Listing(2, 9));
        Assert.Equal("2/9 projects truncated - 7 NOT shown - narrow with filter=", Listing(2, 9));
    });

    [Fact]
    public void Offer_ForConcurrentResponsesOfOneTool_IsPrintedExactlyOnce() => Remembered(() =>
    {
        var offered = 0;

        Parallel.For(0, 64, _ =>
        {
            if (Offered("search_text").Contains("containers=", StringComparison.Ordinal))
                Interlocked.Increment(ref offered);
        });

        Assert.Equal(1, offered);
    });

    private static string Offered(string tool) =>
        new ResponseBuilder(tool, string.Empty).Summary(1, 1, "matches").Line("a.cs:1  x").Offer("containers", "containers=true names it").ToString();

    private static string Condensed() =>
        new ResponseBuilder("read_text", "a.cs").Summary(2, 2, "lines").Offer("condensed", "condensed=true").ToString();

    private static string Listing(int shown, int total) =>
        new ResponseBuilder("list_projects", string.Empty).Summary(shown, total, "projects", "filter=").ToString();

    private static void Remembered(Action test)
    {
        OfferMemo.Forget();
        OfferMemo.Remember(true);

        try
        {
            test();
        }
        finally
        {
            OfferMemo.Remember(false);
            OfferMemo.Forget();
        }
    }
}

[CollectionDefinition(nameof(OfferMemoCollection), DisableParallelization = true)]
public sealed class OfferMemoCollection;
