using System.Globalization;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class SnippetSearchTests
{
    private const string Source = "class A\n{\n    public int Value()\n    {\n        return 1;\n    }\n}";

    private const string Dedented = "public int Value()\n{\n    return 1;\n}";

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

    [Fact]
    public void Find_ForAnAnchorWithoutTheBlankLinesItSpans_MatchesLooselyWithTheBlocksIndent()
    {
        var match = SnippetSearch.Find("    a();\n\n    b();\n", "a();\nb();", 1);

        Assert.True(match.Loose);
        Assert.Equal(1, match.Occurrences);
        Assert.Equal(0, match.Start);
        Assert.Equal("    a();\n\n    b();".Length, match.Length);
        Assert.Equal("    ", match.Indent);
    }

    [Fact]
    public void Find_ForAnAnchorDeeperThanTheFile_DoesNotMatchLoosely()
    {
        var match = SnippetSearch.Find("a();\n\nb();\n", "    a();\n    b();", 1);

        Assert.Equal(0, match.Occurrences);
        Assert.Equal(-1, match.Start);
    }

    [Fact]
    public void Find_ForADedentedAnchorEndingMidLine_MatchesThroughTheLinePrefixAtOneOffset()
    {
        const string Text = "class A\n{\n    void M()\n    {\n        return Call(a,\n            b) + 1;\n    }\n}\n";
        var match = SnippetSearch.Find(Text, "return Call(a,\n    b)", 1);

        Assert.Equal(1, match.Occurrences);
        Assert.Equal("        ", match.Indent);
        Assert.False(match.MidLine);
        Assert.Equal("        return Call(a,\n            b)", Text[match.Start..(match.Start + match.Length)]);
    }

    [Fact]
    public void Find_ForADedentedAnchorStartingMidLine_StartsAtTheColumnAndFlagsMidLine()
    {
        const string Text = "class A\n{\n    int M() => Call(a,\n        b);\n}\n";
        var match = SnippetSearch.Find(Text, "Call(a,\n    b);", 1);

        Assert.Equal(1, match.Occurrences);
        Assert.True(match.MidLine);
        Assert.Equal("    ", match.Indent);
        Assert.Equal("Call(a,\n        b);", Text[match.Start..(match.Start + match.Length)]);
    }

    [Fact]
    public void Find_ForAnAnchorWhoseFirstLineLostItsIndentation_ReindentsTheRestByTheirCommonOffset()
    {
        const string Text = "class A\n{\n    void M()\n    {\n        if (x)\n        {\n            y();\n        }\n    }\n}\n";
        var match = SnippetSearch.Find(Text, "if (x)\n    {\n        y();\n    }", 1);

        Assert.Equal(1, match.Occurrences);
        Assert.False(match.Loose);
        Assert.True(match.MidLine);
        Assert.Equal("    ", match.Indent);
        Assert.Equal("if (x)\n        {\n            y();\n        }", Text[match.Start..(match.Start + match.Length)]);
    }

    [Fact]
    public void Find_ForAMidLineAnchorWhoseLinesDriftToDifferentDepths_StaysUnmatched() =>
        Assert.Equal(0, SnippetSearch.Find("    int M() => Call(a,\n        b,\n      c);\n", "Call(a,\n    b,\n    c);", 1).Occurrences);

    [Fact]
    public void Find_ForAMidLineAnchorThatFitsTwoPlaces_ReportsBothSoTheEditIsRefused()
    {
        var match = SnippetSearch.Find("    x = Call(a,\n        b);\n    y = Call(a,\n        b);\n", "Call(a,\n    b);", 1);

        Assert.Equal(2, match.Occurrences);
        Assert.False(match.IsUnique);
    }

    [Fact]
    public void Find_ForTheSecondOccurrenceOfADedentedAnchor_AnswersTheSecondRegion()
    {
        var twice = Source + "\n\nclass B\n{\n    public int Value()\n    {\n        return 1;\n    }\n}";
        var match = SnippetSearch.Find(twice, Dedented, 2);

        Assert.Equal(2, match.Occurrences);
        Assert.Equal(twice.LastIndexOf("    public int Value()", StringComparison.Ordinal), match.Start);
        Assert.Equal("    ", match.Indent);
    }

    [Fact]
    public void Find_ForAnAnchorStartingInsideTheLinesIndentation_WidensToTheLineAndCarriesTheMissingIndent()
    {
        const string Text = "class A\n{\n    void M()\n    {\n        Foo();\n    }\n}\n";
        var match = SnippetSearch.Find(Text, "    Foo();\n", 1);

        Assert.Equal(1, match.Occurrences);
        Assert.Equal("    ", match.Indent);
        Assert.Equal("        Foo();\n", Text[match.Start..(match.Start + match.Length)]);
    }

    [Fact]
    public void Depth_TreatsExactlyTheCharactersCharIsWhiteSpaceAccepts_AsIndentation()
    {
        var differing = Enumerable.Range(0, char.MaxValue + 1)
            .Select(code => (char)code)
            .Where(character => (SnippetSearch.Depth([character]) is 1) != char.IsWhiteSpace(character)
                || (MemoryExtensions.IndexOfAnyWhiteSpace([character]) is 0) != char.IsWhiteSpace(character)
                || (MemoryExtensions.LastIndexOfAnyWhiteSpace([character]) is 0) != char.IsWhiteSpace(character))
            .Select(character => ((int)character).ToString("X4", CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Empty(differing);
    }

    private const string Block = "class A\n{\n    void M()\n    {\n        var a = 1;\n        var b = 2;\n        var c = 3;\n    }\n}\n";

    [Fact]
    public void Find_ForAnAnchorWhoseFirstLineSitsDeeperThanTheFile_MatchesTheRestAtTheirCommonShift()
    {
        var match = SnippetSearch.Find(Block, "            var a = 1;\n    var b = 2;\n    var c = 3;", 1);

        Assert.Equal(1, match.Occurrences);
        Assert.True(match.Shifted);
        Assert.True(match.MidLine);
        Assert.Equal("    ", match.Indent);
        Assert.Equal(0, match.Outdent);
        Assert.Equal("var a = 1;\n        var b = 2;\n        var c = 3;", Block[match.Start..(match.Start + match.Length)]);
    }

    [Fact]
    public void Find_ForAnAnchorDeeperThanTheFileThroughout_MatchesWithAnOutdent()
    {
        var match = SnippetSearch.Find(Block, "            var a = 1;\n            var b = 2;", 1);

        Assert.Equal(1, match.Occurrences);
        Assert.True(match.Shifted);
        Assert.Null(match.Indent);
        Assert.Equal(4, match.Outdent);
        Assert.Equal("var a = 1;\n        var b = 2;", Block[match.Start..(match.Start + match.Length)]);
    }

    [Fact]
    public void Find_ForADeeperAnchorWhoseLinesDrift_StillMatchesNothing()
    {
        var match = SnippetSearch.Find(Block, "            var a = 1;\n    var b = 2;\n  var c = 3;", 1);

        Assert.Equal(0, match.Occurrences);
        Assert.Equal(-1, match.Start);
    }

    [Fact]
    public void Find_ForADeeperAnchorThatOccursTwice_CountsBothAndAnswersTheSecondOnRequest()
    {
        const string Text = "class A\n{\n    void M()\n    {\n        var a = 1;\n        var b = 2;\n    }\n\n    void N()\n    {\n        var a = 1;\n        var b = 2;\n    }\n}\n";
        var first = SnippetSearch.Find(Text, "            var a = 1;\n            var b = 2;", 1);
        var second = SnippetSearch.Find(Text, "            var a = 1;\n            var b = 2;", 2);

        Assert.Equal(2, first.Occurrences);
        Assert.False(first.IsUnique);
        Assert.Equal(Text.LastIndexOf("var a = 1;", StringComparison.Ordinal), second.Start);
    }

    [Fact]
    public void Find_ForACrlfFileAndADeeperFirstLine_MapsBackOntoTheOriginalOffsets()
    {
        var text = Block.ReplaceLineEndings("\r\n");
        var match = SnippetSearch.Find(text, "            var a = 1;\n    var b = 2;\n    var c = 3;", 1);

        Assert.Equal(1, match.Occurrences);
        Assert.True(match.Shifted);
        Assert.Equal("    ", match.Indent);
        Assert.Equal("var a = 1;\r\n        var b = 2;\r\n        var c = 3;", text[match.Start..(match.Start + match.Length)]);
    }
}
