using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class WorkspaceExclusionTests
{
    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData(".git")]
    [InlineData(".claude")]
    [InlineData(".vs")]
    [InlineData(".idea")]
    [InlineData("artifacts")]
    [InlineData("TestResults")]
    [InlineData("node_modules")]
    public void IsExcludedDirectory_ForAnOutputOrToolDirectory_IsTrue(string name) =>
        Assert.True(WorkspaceFiles.IsExcludedDirectory(name));

    [Theory]
    [InlineData("src")]
    [InlineData("tests")]
    [InlineData("Views")]
    public void IsExcludedDirectory_ForASourceDirectory_IsFalse(string name) =>
        Assert.False(WorkspaceFiles.IsExcludedDirectory(name));

    [Fact]
    public void Traversable_ForANestedAgentWorktree_IsFalse() =>
        Assert.False(WorkspaceFiles.Traversable(Path.Combine(Path.GetTempPath(), ".claude")));
}
