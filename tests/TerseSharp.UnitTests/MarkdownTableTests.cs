using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class MarkdownTableTests
{
    private const string Table = """
        # Backlog

        ## Open

        | Finding | Tool | Proposed change |
        |---|---|---|
        | **I1** first | read_text | fold it |
        | **I2** second | analyze | name it |

        ## Closed

        | Finding | Tool | Outcome |
        |---|---|---|
        | **I0** older | build | shipped |
        """;

    [Fact]
    public void Projected_KeepsOnlyTheNamedColumn()
    {
        var answer = MarkdownTable.Projected("IMPROVEMENTS.md", Table, ["Finding"]).Value!;

        Assert.Contains("**I1** first", answer, StringComparison.Ordinal);
        Assert.Contains("**I2** second", answer, StringComparison.Ordinal);
        Assert.Contains("**I0** older", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("fold it", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("read_text", answer, StringComparison.Ordinal);
    }

    [Fact]
    public void Projected_CountsOneRowPerTableRowAndSkipsTheDelimiter()
    {
        var answer = MarkdownTable.Projected("IMPROVEMENTS.md", Table, ["Finding"]).Value!;

        Assert.StartsWith("3 rows", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("---", answer, StringComparison.Ordinal);
    }

    [Fact]
    public void Projected_KeepsSeveralColumnsInTheOrderAsked()
    {
        var answer = MarkdownTable.Projected("IMPROVEMENTS.md", Table, ["Tool", "Finding"]).Value!;

        Assert.Contains("read_text | **I1** first", answer, StringComparison.Ordinal);
    }

    [Fact]
    public void Projected_ForAColumnNoTableDeclares_NamesTheColumnsThatExist()
    {
        var refusal = MarkdownTable.Projected("IMPROVEMENTS.md", Table, ["Severity"]);

        Assert.False(refusal.IsOk);
        Assert.Contains("names no column", refusal.Error!.Message, StringComparison.Ordinal);
        Assert.Contains("Finding", refusal.Error!.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public void Projected_ForAFileWithNoTable_SaysSo()
    {
        var refusal = MarkdownTable.Projected("README.md", "# Title\n\nprose only\n", ["Finding"]);

        Assert.False(refusal.IsOk);
        Assert.Contains("holds no markdown table", refusal.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Projected_KeepsAnEscapedPipeInsideACell()
    {
        var table = "| Finding | Tool |\n|---|---|\n| a \\| b | read_text |\n";
        var answer = MarkdownTable.Projected("x.md", table, ["Finding"]).Value!;

        Assert.Contains("a \\| b", answer, StringComparison.Ordinal);
    }
}
