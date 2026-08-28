using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class SnippetSearchTests
{
    private const string Source = """
        class A
        {
            public int Value()
            {
                return 1;
            }
        }
        """;

    private const string Dedented = """
        public int Value()
        {
            return 1;
        }
        """;

    [Fact]
    public void Find_ForADedentedAnchor_MatchesTheIndentedRegion()
    {
        var match = SnippetSearch.Find(Source, Dedented, 1);

        Assert.Equal(1, match.Occurrences);
        Assert.Equal("    ", match.Indent);
        Assert.Equal(
            "    public int Value()\n    {\n        return 1;\n    }",
            Source[match.Start..(match.Start + match.Length)]);
    }

    [Fact]
    public void Find_ForADedentedAnchorThatOccursTwice_ReportsBothOccurrences()
    {
        var twice = Source + "\n\nclass B\n{\n    public int Value()\n    {\n        return 1;\n    }\n}";

        Assert.Equal(2, SnippetSearch.Find(twice, Dedented, 1).Occurrences);
    }

    [Fact]
    public void Find_ForAnAnchorWhoseExtraPrefixIsNotWhitespace_DoesNotMatch()
    {
        const string Padded = "class A\n{\nzzzzint V()\nzzzz{\nzzzz    return 1;\nzzzz}\n}\n";

        Assert.Equal(0, SnippetSearch.Find(Padded, "int V()\n{\n    return 1;\n}", 1).Occurrences);
    }

    [Fact]
    public void Find_ForASingleLineAnchor_IsAnsweredByTheExactSearchWithNoIndent()
    {
        var match = SnippetSearch.Find("class A\n{\n    return 1;\n}\n", "return 1;", 1);

        Assert.Equal(1, match.Occurrences);
        Assert.Null(match.Indent);
    }

    [Fact]
    public void Find_ForAnExactMatch_ReportsNoIndent()
    {
        var match = SnippetSearch.Find("int V() => 1;\n", "int V() => 1;", 1);

        Assert.Equal(1, match.Occurrences);
        Assert.Null(match.Indent);
    }

    [Fact]
    public void Find_ForACrlfFileAndADedentedAnchor_MapsBackOntoTheOriginalOffsets()
    {
        const string Crlf = "class A\r\n{\r\n    int V()\r\n    {\r\n        return 1;\r\n    }\r\n}\r\n";
        var match = SnippetSearch.Find(Crlf, "int V()\n{\n    return 1;\n}", 1);

        Assert.Equal(1, match.Occurrences);
        Assert.Equal("    ", match.Indent);
        Assert.Equal("    int V()\r\n    {\r\n        return 1;\r\n    }", Crlf[match.Start..(match.Start + match.Length)]);
    }

    [Fact]
    public void Find_ForAnAnchorEndingWithANewline_ConsumesTheFilesLineBreak()
    {
        const string Text = "class A\n{\n    int V()\n    {\n        return 1;\n    }\n}\n";
        var match = SnippetSearch.Find(Text, "int V()\n{\n    return 1;\n}\n", 1);

        Assert.Equal(1, match.Occurrences);
        Assert.Equal("    int V()\n    {\n        return 1;\n    }\n", Text[match.Start..(match.Start + match.Length)]);
    }

    [Fact]
    public void Find_ForAnAnchorNoIndentationCanReconcile_StaysUnmatched() => Assert.Equal(0, SnippetSearch.Find(Source, "public int Other()\n{\n}", 1).Occurrences);

    [Fact]
    public void NearestRegion_ForAMultiLineAnchorThatDrifted_NamesTheRegionAndItsLineRange()
    {
        const string Text = "# Title\n\nalpha\nbravo\ncharlie\ndelta\n";
        var region = SnippetSearch.NearestRegion(Text, "alpha\nbravo\nECHO");

        Assert.Contains("lines 3-5", region, StringComparison.Ordinal);
        Assert.Contains("2 of the anchor's 3 lines match", region, StringComparison.Ordinal);
        Assert.Contains("startLine=3 endLine=5", region, StringComparison.Ordinal);
    }

    [Fact]
    public void NearestRegion_ForASingleLineAnchor_StaysSilentSoTheLineHitsAnswer() => Assert.Equal(string.Empty, SnippetSearch.NearestRegion("alpha\nbravo\n", "alpha"));

    [Fact]
    public void NearestRegion_WhenNoRegionResembles_StaysSilent() => Assert.Equal(string.Empty, SnippetSearch.NearestRegion("alpha\nbravo\ncharlie\n", "xxx\nyyy\nzzz"));

    [Fact]
    public void Find_ForTheSameBlockAtTwoDifferentIndents_ReportsBothSoTheEditIsRefused()
    {
        const string Twice = "class A\n{\n    int V()\n    {\n        return 1;\n    }\n}\n\nclass B\n{\n        int V()\n        {\n            return 1;\n        }\n}\n";
        var match = SnippetSearch.Find(Twice, "int V()\n{\n    return 1;\n}", 1);

        Assert.Equal(2, match.Occurrences);
        Assert.False(match.IsUnique);
    }

    [Fact]
    public void Find_ForATabIndentedFile_AdoptsTheTabAsTheIndent()
    {
        var match = SnippetSearch.Find("class A\n{\n\tint V()\n\t{\n\t\treturn 1;\n\t}\n}\n", "int V()\n{\n\treturn 1;\n}", 1);

        Assert.Equal(1, match.Occurrences);
        Assert.Equal("\t", match.Indent);
    }

    [Fact]
    public void Find_ForAnAnchorReachingPastTheEndOfTheFile_StaysUnmatched() => Assert.Equal(0, SnippetSearch.Find("    a\n    b\n", "a\nb\nc", 1).Occurrences);

    [Fact]
    public void Find_ForAnAnchorWhoseFirstLineIsBlank_StillAdoptsTheIndentFromTheFirstRealLine()
    {
        var match = SnippetSearch.Find("class A\n{\n\n    int V()\n    {\n        return 1;\n    }\n}\n", "\nint V()\n{\n    return 1;\n}", 1);

        Assert.Equal(1, match.Occurrences);
        Assert.Equal("    ", match.Indent);
    }

    [Fact]
    public void Find_WhenTheFileLineIsShorterThanTheAnchorLine_StaysUnmatched() => Assert.Equal(0, SnippetSearch.Find("class A\n{\n  int V()\n  {\n  }\n}\n", "int V()\n{\n    return 1;\n}", 1).Occurrences);
}
