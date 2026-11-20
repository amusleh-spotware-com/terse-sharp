using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class TerseConfigFileTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "terse-tests", Guid.NewGuid().ToString());

    public TerseConfigFileTests()
    {
        Directory.CreateDirectory(Path.Combine(Repository, ".git"));
        Directory.CreateDirectory(Nested);
        Directory.CreateDirectory(Home);
    }

    private string Repository => Path.Combine(root, "repo");

    private string Nested => Path.Combine(Repository, "src", "App");

    private string Home => Path.Combine(root, "home");

    [Fact]
    public async Task Chain_ListsTheHomeFileFirstAndTheNearestFileLast()
    {
        var global = Written(Home);
        var repository = Written(Repository);
        var nested = Written(Nested);

        string[] expected = [global, repository, nested];

        Assert.Equal(expected, await TerseConfigFile.ChainAsync(Nested, Home, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Chain_WithNoFileUnderTheRepository_StillCarriesTheHomeFile()
    {
        string[] expected = [Written(Home)];

        Assert.Equal(expected, await TerseConfigFile.ChainAsync(Nested, Home, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Chain_WithNeitherFile_IsEmpty() => Assert.Empty(await TerseConfigFile.ChainAsync(Nested, Home, TestContext.Current.CancellationToken));

    [Fact]
    public async Task Chain_WithNoHomeDirectory_CarriesOnlyWhatTheRepositoryDeclares()
    {
        string[] expected = [Written(Repository)];

        Written(Home);

        Assert.Equal(expected, await TerseConfigFile.ChainAsync(Nested, home: null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Chain_WithAFileAboveTheRepositoryRoot_LeavesItOutAndKeepsTheHomeFile()
    {
        Written(root);

        string[] expected = [Written(Home)];

        Assert.Equal(expected, await TerseConfigFile.ChainAsync(Nested, Home, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Chain_ForADirectoryThatDoesNotExist_IsEmpty() =>
        Assert.Empty(await TerseConfigFile.ChainAsync(Path.Combine(root, "absent"), home: null, TestContext.Current.CancellationToken));

    [Fact]
    public async Task PolicyChain_FromALinkedWorktree_CarriesTheMainCheckoutsFilesBelowItsOwnAndAboveTheHomeFile()
    {
        var worktree = Linked("worktrees", "../..\n");
        var global = Written(Home);
        var main = Written(Repository);
        var mainNested = Written(Nested);
        var own = Written(worktree);

        string[] expected = [global, main, mainNested, own];
        string[] settings = [global, own];

        Assert.Equal(expected, await TerseConfigFile.PolicyChainAsync(Path.Combine(worktree, "src", "App"), Home, TestContext.Current.CancellationToken));
        Assert.Equal(settings, await TerseConfigFile.ChainAsync(Path.Combine(worktree, "src", "App"), Home, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PolicyChain_FromASubmoduleWhoseGitIsAFile_LeavesTheSuperprojectsFilesOut()
    {
        var submodule = Linked("modules", null);
        Written(Repository);

        Assert.Empty(await TerseConfigFile.PolicyChainAsync(Path.Combine(submodule, "src", "App"), home: null, TestContext.Current.CancellationToken));
    }

    private string Linked(string kind, string? commonDirectory)
    {
        var checkout = Path.Combine(root, "linked");
        var own = Path.Combine(Repository, ".git", kind, "linked");

        Directory.CreateDirectory(own);
        Directory.CreateDirectory(Path.Combine(checkout, "src", "App"));
        File.WriteAllText(Path.Combine(checkout, ".git"), "gitdir: ../repo/.git/" + kind + "/linked\n");

        if (commonDirectory is not null)
            File.WriteAllText(Path.Combine(own, "commondir"), commonDirectory);

        return checkout;
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static string Written(string directory)
    {
        var path = Path.Combine(directory, TerseConfigFile.FileName);

        File.WriteAllText(path, "{}");

        return path;
    }
}
