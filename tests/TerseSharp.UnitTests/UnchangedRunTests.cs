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
}
