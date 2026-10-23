using TerseSharp.Server.Tools;

namespace TerseSharp.UnitTests;

public sealed class GitLayoutTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("terse-gitlayout-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public async Task GitLayoutAsync_ForALinkedWorktreesGitFile_ResolvesItsOwnDirectoryAndTheCommonOne()
    {
        var common = Path.Combine(root, "main", ".git");
        var own = Path.Combine(common, "worktrees", "wt");
        var nested = Path.Combine(root, "wt", "sub");

        Directory.CreateDirectory(own);
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(root, "wt", ".git"), "gitdir: ../main/.git/worktrees/wt\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(own, "commondir"), "../..\n", TestContext.Current.CancellationToken);

        Assert.Equal<GitTools.GitLayout?>(new GitTools.GitLayout(own, common), await GitTools.GitLayoutAsync(nested, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GitLayoutAsync_ForAGitDirectory_AnswersItAsBothItsOwnAndTheCommonDirectory()
    {
        var git = Path.Combine(root, ".git");

        Directory.CreateDirectory(git);

        Assert.Equal<GitTools.GitLayout?>(new GitTools.GitLayout(git, git), await GitTools.GitLayoutAsync(root, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GitLayoutAsync_ForAGitFilePointingAtNoDirectory_AnswersNull()
    {
        await File.WriteAllTextAsync(Path.Combine(root, ".git"), "gitdir: missing\n", TestContext.Current.CancellationToken);

        Assert.Null(await GitTools.GitLayoutAsync(root, TestContext.Current.CancellationToken));
    }
}
