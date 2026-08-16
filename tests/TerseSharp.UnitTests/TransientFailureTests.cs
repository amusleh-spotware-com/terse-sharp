using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class TransientFailureTests
{
    [Fact]
    public void IsBuildHostFailure_RecognisesTheRpcTypesMsBuildsOutOfProcessHostThrows()
    {
        Assert.True(Errors.IsBuildHostFailure(new RemoteInvocationException("the pipe is broken")));
        Assert.True(Errors.IsBuildHostFailure(new InvalidOperationException("apply failed", new RemoteInvocationException("gone"))));
        Assert.True(Errors.IsBuildHostFailure(new ConnectionLostException("gone")));
    }

    [Fact]
    public void IsBuildHostFailure_LeavesEveryOtherFailureAlone()
    {
        Assert.False(Errors.IsBuildHostFailure(new InvalidOperationException("plain")));
        Assert.False(Errors.IsBuildHostFailure(new IOException("locked", new UnauthorizedAccessException())));
    }

    [Fact]
    public void Transient_RendersItsOwnCodeAndTellsTheCallerToRetry()
    {
        var rendered = Errors.Transient(new RemoteInvocationException("the pipe is broken")).Render();

        Assert.StartsWith("ERROR Transient: RemoteInvocationException: the pipe is broken", rendered, StringComparison.Ordinal);
        Assert.Contains("the project file was restored", rendered, StringComparison.Ordinal);
        Assert.Contains("may already be on disk", rendered, StringComparison.Ordinal);
        Assert.Contains("Retry the same call", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("server defect", rendered, StringComparison.Ordinal);
    }

    private sealed class RemoteInvocationException(string message) : Exception(message);

    private sealed class ConnectionLostException(string message) : Exception(message);
}
