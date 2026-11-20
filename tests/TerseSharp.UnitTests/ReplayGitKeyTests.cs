using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ReplayGitKeyTests
{
    [Fact]
    public void GitKey_ForTheSameRefAndGitStates_IsIdenticalSoARepeatReplays() =>
        Assert.Equal(ReplayGate.GitKey("analyze", "HEAD", ["C:/r@index=1:2 HEAD=abc"]), ReplayGate.GitKey("analyze", "HEAD", ["C:/r@index=1:2 HEAD=abc"]));

    [Fact]
    public void GitKey_WhenTheRefResolvesToAnotherCommit_Differs() =>
        Assert.NotEqual(ReplayGate.GitKey("analyze", "main", ["C:/r@index=1:2 main=abc"]), ReplayGate.GitKey("analyze", "main", ["C:/r@index=1:2 main=def"]));

    [Fact]
    public void GitKey_IsNeverThePlainKeySoAGitCallCannotReplayAnUnnarrowedAnswer() =>
        Assert.NotEqual("analyze", ReplayGate.GitKey("analyze", "HEAD", ["C:/r@s HEAD=abc"]));

    [Fact]
    public void GitKey_WhenAnyWorkspaceGitStateIsUnknown_IsNullSoTheCallRuns() =>
        Assert.Null(ReplayGate.GitKey("analyze", "HEAD", ["C:/a@s HEAD=abc", null]));

    [Fact]
    public void GitKey_WithNoLoadedWorkspace_IsNullSoTheCallRuns() =>
        Assert.Null(ReplayGate.GitKey("analyze", "HEAD", []));

    [Theory]
    [InlineData(null, true, "HEAD")]
    [InlineData(null, false, null)]
    [InlineData("", true, null)]
    [InlineData("main", false, "main")]
    public void GitKeyed_ResolvesTheRefTheNarrowingWillDiffAgainst(string? baseRef, bool defaulted, string? expected) =>
        Assert.Equal(expected, ReplayGate.GitKeyed("k", baseRef, defaulted).GitRef);
}
