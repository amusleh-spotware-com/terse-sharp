namespace TerseSharp.UnitTests;

public sealed class BacklogShapeTests
{
    private const int OpenColumns = 5;
    private const int ClosedColumns = 4;

    private static readonly string[] Lines =
        File.ReadAllLines(Path.Combine(Fixtures.RepositoryRoot, "IMPROVEMENTS.md"));

    [Fact]
    public void TheBacklog_CarriesExactlyTheOpenAndClosedSections()
    {
        string[] expected = ["# Improvements backlog", "## Open", "## Closed"];

        var headings = Lines.Where(line => line.StartsWith('#')).ToArray();

        Assert.Equal(expected, headings);
    }

    [Fact]
    public void TheBacklog_CarriesNothingButHeadingsAndTableRows()
    {
        var prose = Lines.Where(line => line.Length is not 0 && line[0] is not '#' and not '|').ToArray();

        Assert.Empty(prose);
    }

    [Fact]
    public void TheBacklog_KeepsTheColumnsBothTablesAreDefinedBy()
    {
        Assert.Contains("| Finding | Tool | Proposed change | Expected saving | Rejected |", Lines);
        Assert.Contains("| Finding | Tool | Change | Outcome |", Lines);
    }

    [Fact]
    public void TheBacklog_KeepsEveryRowInTheShapeItsOwnHeaderDeclares()
    {
        var closed = Array.IndexOf(Lines, "## Closed");

        Assert.All(RowsOf(Lines[..closed]), row => Assert.Equal(OpenColumns, CellsOf(row)));
        Assert.All(RowsOf(Lines[closed..]), row => Assert.Equal(ClosedColumns, CellsOf(row)));
    }

    private static IEnumerable<string> RowsOf(IEnumerable<string> lines) =>
        lines.Where(line => line.StartsWith('|'));

    private static int CellsOf(string row)
    {
        var cells = row.AsSpan().Trim('|');

        return cells.Count('|') - cells.Count("\\|") + 1;
    }
}
