using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class SelfBuildTests
{
    [Fact]
    public void Builds_WhenAProjectOutputIsTheRunningAssembly_SaysSoThroughASeparatorDifference()
    {
        var running = Path.Combine(AppContext.BaseDirectory, "terse.dll");
        var declared = Path.Combine(AppContext.BaseDirectory, "..", Path.GetFileName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)), "terse.dll");

        Assert.True(SelfBuild.Builds([declared], running));
    }

    [Fact]
    public void Builds_WhenNoProjectOutputMatches_SaysNo()
    {
        var running = Path.Combine(AppContext.BaseDirectory, "terse.dll");

        Assert.False(SelfBuild.Builds([Path.Combine(AppContext.BaseDirectory, "other.dll")], running));
        Assert.False(SelfBuild.Builds([], running));
    }

    [Fact]
    public void Refusal_WhenTheSolutionBuildsTheRunningAssembly_NamesMsb3026AndTheCopyToMake()
    {
        var root = AppContext.BaseDirectory;
        var target = new WorkspaceTarget(Path.Combine(root, "Some.slnx"), root, RunningAssembly: Path.Combine(root, "terse.dll"));
        var refusal = SelfBuild.Refusal(target, writes: true);

        Assert.NotNull(refusal);
        Assert.Contains("MSB3026", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("terse.dll", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("copy the tool directory somewhere outside the solution", refusal.Remedy, StringComparison.Ordinal);
        Assert.Null(SelfBuild.Refusal(target, writes: false));
    }

    [Fact]
    public void Refusal_WhenTheSolutionDoesNotBuildTheRunningAssembly_IsNull()
    {
        Assert.Null(SelfBuild.Refusal(new WorkspaceTarget("Some.slnx", AppContext.BaseDirectory), writes: true));
        Assert.Null(SelfBuild.Refusal(new WorkspaceTarget("Some.slnx", AppContext.BaseDirectory), writes: false));
    }
}
