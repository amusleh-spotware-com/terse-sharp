using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class CoChangeTests
{
    private const string Log = "\0\n\nsrc/Own.cs\nsrc/Often.cs\nsrc/Twice.cs\nsrc/Once.cs\n\0\n\nsrc/Own.cs\nsrc/Often.cs\nsrc/Twice.cs\n\0\n\nsrc/Own.cs\nsrc/Often.cs\n\0\n\nsrc/Unrelated.cs\nsrc/Often.cs\nsrc/Once.cs\n";

    [Fact]
    public void Lines_ForFilesThatChangedWithTheDeclaringFile_RanksThemByCommitCountAndCountsOnlyCommitsThatTouchedIt() =>
        Assert.Equal(
            [Path.Join("src", "Often.cs") + "  HEURISTIC  co-changed  commits=3", Path.Join("src", "Twice.cs") + "  HEURISTIC  co-changed  commits=2"],
            CoChange.Lines(Log, [Path.Join("src", "Own.cs")], []));

    [Fact]
    public void Lines_ForAFileThatCoChangedInOnlyOneCommit_LeavesItOut() =>
        Assert.DoesNotContain(CoChange.Lines(Log, ["src/Own.cs"], []), line => line.Contains("Once.cs", StringComparison.Ordinal));

    [Fact]
    public void Lines_ForAFileTheSemanticListAlreadyNames_LeavesItOut() =>
        Assert.Equal(
            [Path.Join("src", "Twice.cs") + "  HEURISTIC  co-changed  commits=2"],
            CoChange.Lines(Log, ["src/Own.cs"], [Path.Join("src", "Often.cs")]));

    [Fact]
    public void Lines_ForMoreThanFiveCandidates_KeepsTheFiveMostFrequentWithTiesInPathOrder()
    {
        var commit = "\0\n\nOwn.cs\nA.cs\nB.cs\nC.cs\nD.cs\nE.cs\nF.cs\n";
        var lines = CoChange.Lines(commit + commit + "\0\n\nOwn.cs\nF.cs\n", ["Own.cs"], []);

        Assert.Equal(["F.cs  HEURISTIC  co-changed  commits=3", "A.cs  HEURISTIC  co-changed  commits=2", "B.cs  HEURISTIC  co-changed  commits=2", "C.cs  HEURISTIC  co-changed  commits=2", "D.cs  HEURISTIC  co-changed  commits=2"], lines);
    }

    [Fact]
    public void Lines_ForALogThatNeverTouchesTheDeclaringFileOrIsEmpty_AnswersNothing()
    {
        Assert.Empty(CoChange.Lines(Log, ["src/Absent.cs"], []));
        Assert.Empty(CoChange.Lines(string.Empty, ["src/Own.cs"], []));
    }

    [Fact]
    public void Lines_ForCrlfOutput_MatchesTheDeclaringFile() =>
        Assert.Single(CoChange.Lines(Log.ReplaceLineEndings("\r\n"), ["src/Own.cs"], [Path.Join("src", "Often.cs")]));
}
