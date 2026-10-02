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
}
