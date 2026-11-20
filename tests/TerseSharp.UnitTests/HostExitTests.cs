using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class HostExitTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(600);

    [Fact]
    public void Early_AHostThatEndedLongBeforeItsHangWindow_IsNotATimeout() =>
        Assert.True(HostExit.Early(new ProcessRun(1, string.Empty, 11_000), Budget, ["Ns.Tests.Crashes"]));

    [Fact]
    public void Early_ARunThatReachedTheHangWindow_StaysAHang() =>
        Assert.False(HostExit.Early(new ProcessRun(1, string.Empty, 590_000), Budget, ["Ns.Tests.Hangs"]));

    [Fact]
    public void Early_ARunThatTimedOut_StaysATimeout() =>
        Assert.False(HostExit.Early(new ProcessRun(1, string.Empty, 11_000, TimedOut: true), Budget, ["Ns.Tests.Hangs"]));

    [Fact]
    public void Early_WithNoSequenceName_HasNothingToReport() =>
        Assert.False(HostExit.Early(new ProcessRun(1, string.Empty, 11_000), Budget, []));

    [Fact]
    public void Render_ForAnEarlyExit_NamesExitCodeElapsedRunningTestsAndReasonAndNeverSaysTimedOut()
    {
        var run = new ProcessRun(1, "Starting test execution\nThe active test run was aborted. Reason: Test host process crashed : Stack overflow.\n", 11_000);

        var text = HostExit.Render("run_tests base", ["TerseSharp.UnitTests"], ["Ns.Tests.Crashes"], run);

        Assert.Contains("WARNING the test host exited early (exitCode=1, elapsedMs=11000)", text, StringComparison.Ordinal);
        Assert.Contains("still running: Ns.Tests.Crashes", text, StringComparison.Ordinal);
        Assert.Contains("\nincomplete project(s): TerseSharp.UnitTests", text, StringComparison.Ordinal);
        Assert.Contains("\nreason: Test host process crashed : Stack overflow.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("timed out", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Reason_WithNoAbortLine_IsEmpty() =>
        Assert.Equal(string.Empty, HostExit.Reason("Passed!  - Failed: 0, Passed: 12"));
}
