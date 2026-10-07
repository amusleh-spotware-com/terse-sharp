using TerseSharp.Core;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ErrorCodeAttributionTests
{
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    [Fact]
    public void Boundary_ASharingViolation_AnswersFileLockedRatherThanAnArgumentError()
    {
        var text = ToolBoundary.Run(() => throw new IOException("The process cannot access the file 'build.log' because it is being used by another process.", SharingViolation));

        Assert.StartsWith("ERROR FileLocked: ", text, StringComparison.Ordinal);
        Assert.Contains("'build.log'", text, StringComparison.Ordinal);
        Assert.Contains("remedy: retry once the other process closes the file", text, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidArgument", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Boundary_ALockViolation_AnswersFileLocked()
    {
        var text = ToolBoundary.Run(() => throw new IOException("locked region", LockViolation));

        Assert.StartsWith("ERROR FileLocked: locked region", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(0)]
    [InlineData(unchecked((int)0x80070002))]
    public void Boundary_AnyOtherIOException_KeepsTodaysArgumentAnswer(int result)
    {
        var text = ToolBoundary.Run(() => throw new IOException("broken pipe", result));

        Assert.StartsWith("ERROR InvalidArgument: IOException: broken pipe", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeout_RendersItsOwnCode_NotInvalidArgument()
    {
        var text = Errors.Timeout("git did not answer", "narrow it").Render();

        Assert.Equal("ERROR Timeout: git did not answer\nremedy: narrow it", text);
    }

    [Fact]
    public void RunNotFound_NamesTheIdAndTellsTheAgentToRunAgain()
    {
        var text = Errors.RunNotFound("t7").Render();

        Assert.StartsWith("ERROR RunNotFound: no detached run has id 't7'", text, StringComparison.Ordinal);
        Assert.Contains("remedy: start the run again", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitRunner_ACancelledRead_AnswersCancelledRatherThanAnArgumentError()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var read = await GitRunner.ReadAsync(Path.GetTempPath(), ["--version"], cancellation.Token);

        Assert.Equal(TerseErrorCode.Cancelled, read.Error!.Code);
        Assert.Contains("the arguments were fine", read.Error.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public void GitRunner_AnUndrainedStream_AnswersIncompleteRatherThanAnArgumentError()
    {
        var read = GitRunner.Answer(new ProcessRun(0, "partial", 12, StandardOutput: "partial", Drained: false));

        Assert.Equal(TerseErrorCode.Incomplete, read.Error!.Code);
        Assert.Contains("the arguments were fine", read.Error.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public void GitRunner_ARunKilledAtTheDeadline_AnswersTimeoutBeforeCancelled()
    {
        var read = GitRunner.Answer(new ProcessRun(-1, "", 60_000, TimedOut: true, Drained: false, Stopped: true));

        Assert.Equal(TerseErrorCode.Timeout, read.Error!.Code);
    }

    [Fact]
    public void GitRunner_ANonZeroExit_KeepsTodaysArgumentAnswer()
    {
        var read = GitRunner.Answer(new ProcessRun(128, "", 5, StandardError: "fatal: bad revision 'nope'\n"));

        Assert.Equal(TerseErrorCode.InvalidArgument, read.Error!.Code);
        Assert.Equal("git exited 128: fatal: bad revision 'nope'", read.Error.Message);
    }
}
