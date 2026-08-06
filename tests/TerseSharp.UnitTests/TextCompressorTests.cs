using TerseSharp.Core;
using Xunit;

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
    public void Source_DropsBlankAndWhitespaceOnlyLines()
    {
        var text = "one\n\n   \ntwo\n";

        Assert.Equal("one\ntwo", TextCompressor.Source(text));
    }

    [Fact]
    public void Source_StripsTrailingWhitespaceWithoutTouchingTheLeadingIndent()
    {
        var text = "  a   \n  b\t\n";

        Assert.Equal("a\nb", TextCompressor.Source(text));
    }

    [Fact]
    public void Source_OnAnEmptyInput_AnswersEmpty() => Assert.Equal(string.Empty, TextCompressor.Source(string.Empty));

    [Fact]
    public void Source_OnWhitespaceOnly_AnswersEmpty() => Assert.Equal(string.Empty, TextCompressor.Source("   \n\t\n"));

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
}
