using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ResponseBuilderTests
{
    [Fact]
    public void Summary_WhenTruncated_NamesTheParameterThatNarrowsIt()
    {
        var text = new ResponseBuilder("search_text", "Order").Summary(2, 9, "matches", "glob=").ToString();

        Assert.Equal("2/9 matches truncated - narrow with glob=", text);
    }

    [Fact]
    public void Summary_WhenNotTruncated_SaysNothingAboutNarrowing()
    {
        var text = new ResponseBuilder("search_text", "Order").Summary(9, 9, "matches", "glob=").ToString();

        Assert.Equal("9 matches", text);
    }

    [Fact]
    public void Summary_WhenTotalExceedsShown_MarksTruncated()
    {
        var text = new ResponseBuilder("find_usages", "M:A.B").Summary(2, 9, "usages").ToString();

        Assert.Equal("2/9 usages truncated", text);
    }

    [Fact]
    public void Summary_WhenAllShown_SaysNothingAboutTruncation() =>
        Assert.Equal("3 items", new ResponseBuilder("t", "a").Summary(3, 3, "items").ToString());

    [Fact]
    public void Lines_AreOneRecordPerLine()
    {
        var text = new ResponseBuilder("t", "a").Summary(2, 2, "items").Line("first").Line("second").ToString();

        Assert.Equal(["2 items", "first", "second"], text.Split('\n'));
    }

    [Fact]
    public void Header_IsDroppedUnlessVerboseIsAsked() =>
        Assert.Equal("x", new ResponseBuilder("list_workspaces", string.Empty).Line("x").ToString());

    [Fact]
    public void Verbose_RestoresTheHeaderAndTheFullSummary()
    {
        var text = new ResponseBuilder("find_usages", "M:A.B").Verbose(true).Summary(2, 9, "usages").Line("first").ToString();

        Assert.Equal(["find_usages M:A.B", "2 usages (truncated=true, total=9)", string.Empty, "first"], text.Split('\n'));
    }

    [Fact]
    public void Verbose_WithoutArgument_LeavesNoTrailingSpaceOnTheHeader() =>
        Assert.StartsWith(
            "list_workspaces\n",
            new ResponseBuilder("list_workspaces", string.Empty).Verbose(true).Line("x").ToString(),
            StringComparison.Ordinal);

    [Fact]
    public void ARecordIsNeverRewritten_EvenWhenEveryRecordSharesAConfidenceTag()
    {
        var text = new ResponseBuilder("t", "a")
            .Summary(2, 2, "usages")
            .Line("a.cs:1  EXACT  one")
            .Line("b.cs:2  EXACT  two")
            .ToString();

        Assert.Equal(["2 usages", "a.cs:1  EXACT  one", "b.cs:2  EXACT  two"], text.Split('\n'));
    }

    [Fact]
    public void ARecordWhoseOwnPayloadContainsATagLiteral_KeepsItByteForByte()
    {
        var payload = "public const string ExactTag = \"  EXACT  \";";
        var text = new ResponseBuilder("get_symbol_source", "M:A.B").Line(payload).ToString();

        Assert.Equal(payload, text);
    }

    [Fact]
    public void EveryRecordKeepsItsWholePath_SoAPathCanBeFedStraightBackToAnotherTool()
    {
        var text = new ResponseBuilder("find_files", "*.cs")
            .Summary(3, 3, "files")
            .Line("src/TerseSharp.Core/ServiceOne.cs")
            .Line("src/TerseSharp.Core/ServiceTwo.cs")
            .Line("src/TerseSharp.Core/ServiceThree.cs")
            .ToString();

        Assert.Equal(
            ["3 files", "src/TerseSharp.Core/ServiceOne.cs", "src/TerseSharp.Core/ServiceTwo.cs", "src/TerseSharp.Core/ServiceThree.cs"],
            text.Split('\n'));
    }

    [Fact]
    public void Summary_WhenTheListingIsLargeButComplete_StillAdvertisesTheNarrowingParameter()
    {
        var text = new ResponseBuilder("list_projects", "A.slnx").Summary(145, 145, "projects", "filter=").ToString();

        Assert.Equal("145 projects - narrow with filter=", text);
    }

    [Fact]
    public void Summary_WhenTheListingIsSmall_StaysSilentAboutNarrowing()
    {
        var text = new ResponseBuilder("list_projects", "A.slnx").Summary(4, 4, "projects", "filter=").ToString();

        Assert.Equal("4 projects", text);
    }

    [Fact]
    public void Summary_WithNoNarrowingParameter_NeverAdvertisesOneHoweverLargeTheListing()
    {
        var text = new ResponseBuilder("list_endpoints", "A.slnx").Summary(900, 900, "endpoints").ToString();

        Assert.Equal("900 endpoints", text);
    }

    [Fact]
    public void Summary_WhenTheCallerChoseNoCap_NamesTheMaxResultsValueThatReturnsTheRest()
    {
        var text = new ResponseBuilder("search_text", "Order").Summary(2, 9, "matches", "glob= or maxResults=").ToString();

        Assert.Equal("2/9 matches truncated - narrow with glob= or maxResults=9", text);
    }

    [Fact]
    public void Summary_WhenTheCallerChoseTheCap_NeitherNamesNorRaisesIt()
    {
        var text = new ResponseBuilder("search_text", "Order").Chosen(true).Summary(2, 9, "matches", "glob= or maxResults=").ToString();

        Assert.Equal("2/9 matches truncated - narrow with glob=", text);
    }
}
