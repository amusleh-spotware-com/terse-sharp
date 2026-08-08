using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class TextCompressorTests
{
    [Fact]
    public void Source_RemovesTheIndentationEveryLineShares()
    {
        var text = "    public int Value()\n    {\n        return 1;\n    }\n";

        Assert.Equal("public int Value()\n{\n    return 1;\n}", TextCompressor.Source(text));
    }

    [Fact]
    public void Source_KeepsTheIndentationWhenOneLineIsAtColumnZero()
    {
        var text = "namespace A;\n    class B;\n";

        Assert.Equal("namespace A;\n    class B;", TextCompressor.Source(text));
    }

    [Fact]
    public void Source_KeepsBlankAndWhitespaceOnlyLinesBecauseTheyMayBeInsideARawStringLiteral()
    {
        var text = "one\n\n   \ntwo\n";

        Assert.Equal("one\n\n   \ntwo", TextCompressor.Source(text));
    }

    [Fact]
    public void Source_DedentsWithoutRewritingAnythingElseOnTheLine()
    {
        var text = "  a   \n  b\t\n";

        Assert.Equal("a   \nb\t", TextCompressor.Source(text));
    }

    [Fact]
    public void Source_OnAnEmptyInput_AnswersEmpty() => Assert.Equal(string.Empty, TextCompressor.Source(string.Empty));

    [Fact]
    public void Source_OnWhitespaceOnly_KeepsItRatherThanInventingAnEmptyPayload() =>
    Assert.Equal("   \n\t", TextCompressor.Source("   \n\t\n"));

    [Theory]
    [InlineData("Program.cs")]
    [InlineData("View.xaml")]
    [InlineData("Page.razor")]
    [InlineData("App.CSPROJ")]
    [InlineData("settings.json")]
    public void KeepsBlankLines_IsFalseForWhitespaceInsignificantFiles(string path) =>
        Assert.False(TextCompressor.KeepsBlankLines(path));

    [Theory]
    [InlineData("README.md")]
    [InlineData("notes.txt")]
    [InlineData("pipeline.yml")]
    [InlineData("script.py")]
    [InlineData("noextension")]
    public void KeepsBlankLines_IsTrueWhereABlankLineCarriesMeaning(string path) =>
        Assert.True(TextCompressor.KeepsBlankLines(path));

    [Fact]
    public void Source_OnARawStringLiteral_LeavesItsBlankLinesAndTrailingSpacesIntact()
    {
        var text = "    var json = \"\"\"\n    {\n\n      \"a\": 1   \n    }\n    \"\"\";\n";

        Assert.Equal("var json = \"\"\"\n{\n\n  \"a\": 1   \n}\n\"\"\";", TextCompressor.Source(text));
    }
}
