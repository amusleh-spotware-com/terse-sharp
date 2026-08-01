using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class MarkdownAnchorTests
{
    [Fact]
    public void Of_WithAPlainHeading_LowercasesAndHyphenates() =>
        Assert.Equal("what-it-saves-you", MarkdownAnchor.Of("## What it saves you"));

    [Fact]
    public void Of_WithAnEmoji_DropsItAndKeepsTheSpaceAsAHyphen() =>
        Assert.Equal("-where-tersesharp-sits", MarkdownAnchor.Of("## 🌉 Where TerseSharp sits"));

    [Fact]
    public void Of_WithPunctuation_DropsIt() =>
        Assert.Equal("razor-and-blazor-resolved", MarkdownAnchor.Of("### Razor and Blazor, resolved."));

    [Fact]
    public void Of_WithBackticks_DropsThem() =>
        Assert.Equal("without-reading-a-single-resx", MarkdownAnchor.Of("## Without reading a single `.resx`"));

    [Fact]
    public void Of_WithAnExistingHyphen_KeepsIt() =>
        Assert.Equal("dead-code-findings", MarkdownAnchor.Of("# Dead-code findings"));

    [Fact]
    public void Of_WithAMarkdownLink_KeepsTheTextAndDropsTheUrl() =>
        Assert.Equal("docs", MarkdownAnchor.Of("## [Docs](https://example.com/a/b)"));

    [Fact]
    public void Of_WithALinkAmongOtherWords_KeepsTheSurroundingWords() =>
        Assert.Equal("see-the-guide-first", MarkdownAnchor.Of("## See the [guide](https://x.y) first"));
}
