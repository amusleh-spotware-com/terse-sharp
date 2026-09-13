using System.Diagnostics;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class UnchangedRunTests
{
    [Fact]
    public void Replay_RendersTheToolItWasRememberedFor()
    {
        var runs = new UnchangedRun();

        runs.Remember("key", "stamp", "build ok  errors=0 warnings=0", 0);

        var replay = runs.Replay("build", "key", "stamp", Stopwatch.Frequency);

        Assert.NotNull(replay);
        Assert.StartsWith("build UNCHANGED", replay, StringComparison.Ordinal);
        Assert.Contains("previous: build ok  errors=0 warnings=0", replay, StringComparison.Ordinal);
        Assert.Contains("force=true re-runs it", replay, StringComparison.Ordinal);
    }

    [Fact]
    public void Replay_WithADifferentStamp_AnswersNothing()
    {
        var runs = new UnchangedRun();

        runs.Remember("key", "stamp-one", "run_tests PASSED", 0);

        Assert.Null(runs.Replay("run_tests", "key", "stamp-two", 1));
    }

    [Fact]
    public void MissReason_ForAKeyNeverRemembered_SaysFirstRun()
    {
        var runs = new UnchangedRun();

        Assert.Equal("first run of this key", runs.MissReason("key", "stamp"));
    }

    [Fact]
    public void MissReason_ForARememberedKeyWhoseStampMoved_NamesTheSegmentThatMoved()
    {
        var runs = new UnchangedRun();

        runs.Remember("key", "pulse=1 root@a=g1", "run_tests PASSED", 0);

        Assert.Equal(
            "stamp moved: remembered 'root@a=g1' current 'root@a=g2'",
            runs.MissReason("key", "pulse=1 root@a=g2"));
    }

    [Fact]
    public void MemoRefusal_ForAMultiLineGreenVerdict_SaysItIsNotASingleLine()
    {
        Assert.Equal(
            "the verdict is not a single line",
            UnchangedRun.MemoRefusal("run_tests PASSED", "run_tests PASSED  passed=1\nextra"));
    }

    [Fact]
    public void MemoRefusal_ForAVerdictThatIsNotGreen_NamesTheExpectedPrefix()
    {
        Assert.Equal(
            "the verdict does not open with 'run_tests PASSED'",
            UnchangedRun.MemoRefusal("run_tests PASSED", "1 failures  build=ok"));
    }

    [Fact]
    public void MemoRefusal_ForASingleLineGreenVerdict_AnswersNothing() => Assert.Null(UnchangedRun.MemoRefusal("build ok", "build ok  errors=0 warnings=0"));
}
