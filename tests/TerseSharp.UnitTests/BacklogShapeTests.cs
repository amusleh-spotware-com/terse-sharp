namespace TerseSharp.UnitTests;

public sealed class BacklogShapeTests
{
    private const int OpenColumns = 5;
    private const int ClosedColumns = 4;
    private const int RowsTheSplitMoved = 319;
    private const string ClosedPointer = "Closed rows: [IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md).";
    private const string OpenPointer = "Open rows: [IMPROVEMENTS.md](IMPROVEMENTS.md).";

    private static readonly string[] Backlog = LinesOf("IMPROVEMENTS.md");
    private static readonly string[] Archive = LinesOf("IMPROVEMENTS-ARCHIVE.md");

    [Fact]
    public void TheBacklog_CarriesTheOpenSectionAlone()
    {
        string[] expected = ["# Improvements backlog", "## Open"];

        Assert.Equal(expected, HeadingsOf(Backlog));
    }

    [Fact]
    public void TheArchive_CarriesTheClosedSectionAlone()
    {
        string[] expected = ["# Improvements archive", "## Closed"];

        Assert.Equal(expected, HeadingsOf(Archive));
    }

    [Fact]
    public void TheBacklog_CarriesNothingButHeadingsRowsAndThePointerToTheArchive()
    {
        string[] expected = [ClosedPointer];

        Assert.Equal(expected, ProseOf(Backlog));
    }

    [Fact]
    public void TheArchive_CarriesNothingButHeadingsRowsAndThePointerToTheBacklog()
    {
        string[] expected = [OpenPointer];

        Assert.Equal(expected, ProseOf(Archive));
    }

    [Fact]
    public void TheBacklogAndTheArchive_KeepTheColumnsTheirOwnTableIsDefinedBy()
    {
        Assert.Contains("| Finding | Tool | Proposed change | Expected saving | Rejected |", Backlog);
        Assert.Contains("| Finding | Tool | Change | Outcome |", Archive);
    }

    [Fact]
    public void TheArchive_StillHoldsEveryClosedRowTheSplitMovedIntoIt() =>
        Assert.True(DataRowsOf(Archive).Length >= RowsTheSplitMoved);

    [Fact]
    public void TheBacklogAndTheArchive_KeepEveryRowInTheShapeItsOwnHeaderDeclares()
    {
        Assert.All(RowsOf(Backlog), row => Assert.Equal(OpenColumns, CellsOf(row)));
        Assert.All(RowsOf(Archive), row => Assert.Equal(ClosedColumns, CellsOf(row)));
    }

    private static string[] LinesOf(string name) =>
        File.ReadAllLines(Path.Combine(Fixtures.RepositoryRoot, name));

    private static string[] HeadingsOf(string[] lines) =>
        [.. lines.Where(line => line.StartsWith('#'))];

    private static string[] ProseOf(string[] lines) =>
        [.. lines.Where(line => line.Length is not 0 && line[0] is not '#' and not '|')];

    private static string[] RowsOf(string[] lines) =>
        [.. lines.Where(line => line.StartsWith('|'))];

    private static string[] DataRowsOf(string[] lines) =>
        [.. RowsOf(lines).Where(row => row.AsSpan().ContainsAnyExcept('|', '-', ' ') &&
                                       !row.StartsWith("| Finding |", StringComparison.Ordinal))];

    private static int CellsOf(string row)
    {
        var cells = row.AsSpan().Trim('|');

        return cells.Count('|') - cells.Count("\\|") + 1;
    }

    [Fact]
    public void TheBacklogAndTheArchive_AllocateEveryIdExactlyOnceAcrossBothFiles()
    {
        var ids = DataRowsOf(Backlog).Concat(DataRowsOf(Archive)).Select(IdOf).OfType<string>().ToArray();

        var duplicated = DuplicatedIds()
            .Where(id => !CollidedBeforeThisGate.Contains(id))
            .ToArray();

        Assert.NotEmpty(ids);
        Assert.True(duplicated.Length is 0, "ids allocated more than once: " + string.Join(", ", duplicated));
    }

    private static string? IdOf(string row)
    {
        var start = row.IndexOf("**I", StringComparison.Ordinal);

        if (start < 0)
            return null;

        var end = row.IndexOf("**", start + 3, StringComparison.Ordinal);

        return end < 0 ? null : row[(start + 2)..end];
    }

    [Fact]
    public void TheHistoricalIdCollisions_OnlyEverShrink()
    {
        var duplicated = DuplicatedIds();

        Assert.All(duplicated, id => Assert.Contains(id, CollidedBeforeThisGate));
        Assert.True(
            duplicated.Length <= CollidedBeforeThisGate.Count,
            "the historical collision set grew; a new duplicate id is a defect, not a historical row");
    }

    private static string[] DuplicatedIds() =>
        [
            .. DataRowsOf(Backlog)
            .Concat(DataRowsOf(Archive))
            .Select(IdOf)
            .OfType<string>()
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key),
    ];

    private static readonly HashSet<string> CollidedBeforeThisGate = new(StringComparer.Ordinal)
    {
        "I28", "I38", "I70", "I74", "I81", "I81/I82", "I93", "I216", "I217", "I314",
    };
}
