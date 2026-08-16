using System.Globalization;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class DocumentOutlineTests
{
    private const string Changelog = """
        # Changelog

        ## [Unreleased]

        ### Added

        - one

        ## [1.0.0]

        ### Added

        - two
        """;

    [Fact]
    public void Locate_ForARepeatedHeadingWithNoOccurrence_NamesTheIndexAndWhereEachCandidateStarts()
    {
        var located = DocumentOutline.Locate(DocumentOutline.Headings(Changelog), "### Added");

        Assert.False(located.IsOk);
        Assert.Equal("'### Added' names 2 sections", located.Error!.Message);
        Assert.Contains("occurrence=1..2", located.Error!.Remedy, StringComparison.Ordinal);
        Assert.Contains("1:line 5", located.Error!.Remedy, StringComparison.Ordinal);
        Assert.Contains("2:line 11", located.Error!.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public void Locate_WithAnOccurrence_PicksThatSectionRatherThanTheFirst()
    {
        var sections = DocumentOutline.Headings(Changelog);

        Assert.Equal(5, DocumentOutline.Locate(sections, "### Added", 1).Value.StartLine);
        Assert.Equal(11, DocumentOutline.Locate(sections, "### Added", 2).Value.StartLine);
    }

    [Fact]
    public void Locate_WithAnOccurrencePastTheLast_NamesTheRangeItCouldHavePicked()
    {
        var located = DocumentOutline.Locate(DocumentOutline.Headings(Changelog), "### Added", 3);

        Assert.False(located.IsOk);
        Assert.Contains("occurrence=3 does not exist", located.Error!.Message, StringComparison.Ordinal);
        Assert.Contains("between 1 and 2", located.Error!.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public void Locate_ForAHeadingThatOccursOnce_StillResolvesWithNoOccurrence()
    {
        var located = DocumentOutline.Locate(DocumentOutline.Headings(Changelog), "## [Unreleased]");

        Assert.True(located.IsOk);
        Assert.Equal(3, located.Value.StartLine);
    }

    [Fact]
    public void Locate_ForMoreCandidatesThanItLists_SaysHowManyItDropped()
    {
        var many = string.Join("\n\n", Enumerable.Range(0, 15).Select(index => "### Added\n\n- " + index.ToString(CultureInfo.InvariantCulture)));
        var located = DocumentOutline.Locate(DocumentOutline.Headings(many), "### Added");

        Assert.False(located.IsOk);
        Assert.Contains("occurrence=1..15", located.Error!.Remedy, StringComparison.Ordinal);
        Assert.Contains("+3 more", located.Error!.Remedy, StringComparison.Ordinal);
    }
}
