using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class TouchedLinesTests
{
    private const string Diff = """
        diff --git a/src/Trading/OrderService.cs b/src/Trading/OrderService.cs
        --- a/src/Trading/OrderService.cs
        +++ b/src/Trading/OrderService.cs
        @@ -10,0 +11,3 @@
        +    public int Added() => 1;
        +
        +    public int AlsoAdded() => 2;
        """;

    [Fact]
    public void Covers_IsTrueOnlyForTheLinesTheDiffAdded()
    {
        var touched = TouchedLines.From(Diff, [], Root);

        Assert.True(touched.Covers("src/Trading/OrderService.cs", 11));
        Assert.True(touched.Covers("src/Trading/OrderService.cs", 13));
        Assert.False(touched.Covers("src/Trading/OrderService.cs", 10));
        Assert.False(touched.Covers("src/Trading/OrderService.cs", 14));
        Assert.False(touched.Covers("src/Trading/Other.cs", 11));
    }

    [Fact]
    public void Covers_ForAFileGitDoesNotTrack_IsTrueOnEveryLine()
    {
        var touched = TouchedLines.From(Diff, ["src/Trading/Fresh.cs"], Root);

        Assert.True(touched.Covers("src/Trading/Fresh.cs", 1));
        Assert.True(touched.Covers("src/Trading/Fresh.cs", 4000));
        Assert.True(touched.Covers(Path.Combine(Root, "src/Trading/Fresh.cs"), 7));
    }

    [Fact]
    public void Covers_TakesTheSamePathAbsoluteOrWorkspaceRelative()
    {
        var touched = TouchedLines.From(Diff, [], Root);

        Assert.True(touched.Covers(Path.GetFullPath(Path.Combine(Root, "src/Trading/OrderService.cs")), 12));
    }

    [Theory]
    [InlineData("TERSE101 info Policy tests\\Probe\\WideTests.cs:109:5: WideTests.Wide - 14 statements exceeds 10", "tests\\Probe\\WideTests.cs", 109)]
    [InlineData("CS8019 info DeadCode src/App/Program.cs:1:1: Unnecessary using directive.", "src/App/Program.cs", 1)]
    public void Locate_ReadsThePathAndLineOutOfARenderedRecord(string record, string path, int line)
    {
        var position = PolicyPosition.Locate(record);

        Assert.NotNull(position);
        Assert.Equal(path, position!.Value.Path);
        Assert.Equal(line, position.Value.Line);
    }

    [Fact]
    public void CoversRecord_KeepsARecordItCannotLocate_RatherThanHidingIt()
    {
        var touched = TouchedLines.From(Diff, [], Root);

        Assert.True(touched.CoversRecord("a record with no position at all"));
        Assert.True(touched.CoversRecord("TERSE101 info Policy src/Trading/OrderService.cs:11:5: OrderService.Added - too long"));
        Assert.False(touched.CoversRecord("TERSE101 info Policy src/Trading/OrderService.cs:40:5: OrderService.Old - too long"));
    }

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "terse-touched");

    [Fact]
    public void Covers_TakesAWindowsShapedRelativePath_TheWayARenderedRecordSpellsIt()
    {
        var touched = TouchedLines.From(Diff, ["src\\Trading\\Fresh.cs"], Root);

        Assert.True(touched.Covers("src\\Trading\\OrderService.cs", 12));
        Assert.False(touched.Covers("src\\Trading\\OrderService.cs", 40));
        Assert.True(touched.Covers("src/Trading/Fresh.cs", 4000));
    }

    [Fact]
    public void CoversRecord_ForARecordSpelledWithBackslashes_KeepsWhatTheTreeChangedAndDropsWhatItDidNot()
    {
        var touched = TouchedLines.From(Diff, [], Root);

        Assert.True(touched.CoversRecord("TERSE101 info Policy src\\Trading\\OrderService.cs:11:5: OrderService.Added - too long"));
        Assert.False(touched.CoversRecord("TERSE101 info Policy src\\Trading\\OrderService.cs:40:5: OrderService.Old - too long"));
    }
}
