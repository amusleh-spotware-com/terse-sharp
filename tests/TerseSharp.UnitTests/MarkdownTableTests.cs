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

    [Fact]
    public void Projected_ForOneKnownAndOneUnknownColumn_RefusesNamingOnlyTheUnknownOne()
    {
        var refusal = MarkdownTable.Projected("IMPROVEMENTS.md", Table, ["Finding", "Severity"]);

        Assert.False(refusal.IsOk);
        Assert.Contains("Severity", refusal.Error!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Finding,Severity", refusal.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Projected_ForARefusal_NamesEachColumnOnce()
    {
        var refusal = MarkdownTable.Projected("IMPROVEMENTS.md", Table, ["Severity"]);

        Assert.Equal(1, Occurrences(refusal.Error!.Remedy, "Finding"));
        Assert.Equal(1, Occurrences(refusal.Error!.Remedy, "Tool"));
    }

    [Fact]
    public void Projected_ForAColumnOnlyOneTableDeclares_ProjectsThatTable()
    {
        var answer = MarkdownTable.Projected("IMPROVEMENTS.md", Table, ["Outcome"]);

        Assert.True(answer.IsOk);
        Assert.Contains("shipped", answer.Value!, StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;

        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;

        return count;
    }

    [Fact]
    public void Projected_WithinALineWindow_ReadsOnlyThatSectionsTable()
    {
        var answer = MarkdownTable.Projected("IMPROVEMENTS.md", Table, ["Finding"], 3, 9).Value!;

        Assert.StartsWith("2 rows", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("**I0** older", answer, StringComparison.Ordinal);
    }

    [Fact]
    public void Projected_WhenTheRowsExceedTheCap_ReportsWhatItTruncated()
    {
        var answer = MarkdownTable.Projected("IMPROVEMENTS.md", Table, ["Finding"], 0, 0, 2).Value!;

        Assert.StartsWith("2/3 rows truncated", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("**I0** older", answer, StringComparison.Ordinal);
    }

    [Fact]
    public void Projected_ForARefusalWithinASection_NamesThatSectionAndTheWayOut()
    {
        var refusal = MarkdownTable.Projected("IMPROVEMENTS.md", Table, ["Outcome"], 3, 9, 0, "## Open");

        Assert.False(refusal.IsOk);
        Assert.Contains("section '## Open' of IMPROVEMENTS.md", refusal.Error!.Message, StringComparison.Ordinal);
        Assert.Contains("drop section=", refusal.Error!.Remedy, StringComparison.Ordinal);
    }
}
