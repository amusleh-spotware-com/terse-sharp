using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class WorkspaceExclusionTests
{
    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData(".git")]
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

    [Theory]
    [InlineData(".claude/commands/ship.md", false)]
    [InlineData(".claude/agents/reviewer.md", false)]
    [InlineData(".claude/skills/terse/SKILL.md", false)]
    [InlineData(".claude/hooks/guard.py", false)]
    [InlineData("src/App/.claude/skills/terse/SKILL.md", false)]
    [InlineData(".claude/settings.json", true)]
    [InlineData(".claude/settings.local.json", true)]
    [InlineData(".claude/worktrees/agent-a/src/App/Order.cs", true)]
    [InlineData(".claude/todos/session.json", true)]
    [InlineData(".claude/shell-snapshots/snapshot.sh", true)]
    [InlineData("src/App/.claude/todos/session.json", true)]
    [InlineData("nested/deep/.claude/shell-snapshots/snapshot.sh", true)]
    public void IsExcluded_UnderClaude_KeepsTheAuthoredFilesAndDropsTheSessionState(string relative, bool excluded)
    {
        var root = Path.GetTempPath();
        var file = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

        Assert.Equal(excluded, WorkspaceFiles.IsExcluded(file, root));
    }

    [Fact]
    public void Traversable_UnderClaude_KeepsTheAuthoredSubtreesAndDropsTheSessionState()
    {
        var temporary = Directory.CreateTempSubdirectory("terse-claude");
        var claude = temporary.CreateSubdirectory(".claude");

        try
        {
            Assert.True(WorkspaceFiles.Traversable(claude.FullName), claude.FullName);
            Assert.True(WorkspaceFiles.Traversable(claude.CreateSubdirectory("commands").FullName));
            Assert.True(WorkspaceFiles.Traversable(claude.CreateSubdirectory("agents").FullName));
            Assert.False(WorkspaceFiles.Traversable(claude.CreateSubdirectory("worktrees").FullName));
            Assert.False(WorkspaceFiles.Traversable(claude.CreateSubdirectory("todos").FullName));
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }
}
