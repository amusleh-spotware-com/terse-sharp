namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class WorkspaceResolutionE2ETests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SearchTextAndFindFiles_WithAGlobOnlyOneLoadedRootHolds_AnswerFromItInsteadOfAmbiguousWorkspace()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: false, Token, WithNotesAsync);

        await solution.CallAsync("load_workspace", new() { ["path"] = Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx") });

        var searched = await solution.CallAsync("search_text", new() { ["query"] = "only here", ["glob"] = "notes/only-here.md" });
        var listed = await solution.CallAsync("find_files", new() { ["glob"] = "notes/*.md" });
        var shared = await solution.CallAsync("search_text", new() { ["query"] = "OrderService", ["glob"] = "src/**/*.cs" });

        Assert.Contains("notes/only-here.md:1", searched.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Contains("notes/only-here.md", listed.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.DoesNotContain("AmbiguousWorkspace", searched + listed, StringComparison.Ordinal);
        Assert.StartsWith("ERROR AmbiguousWorkspace", shared, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchSymbols_WithAWorktreeNameTwoLoadedSolutionsShare_AnswersFromTheOneAtItsRootAndSaysSo()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: false, Token, WithNestedSolutionAsync);
        var worktree = Path.GetFileName(solution.Root);

        await solution.CallAsync("load_workspace", new() { ["path"] = Path.Combine(solution.Root, "nested", "Nested.slnx") });

        var found = await solution.CallAsync("search_symbols", new() { ["query"] = "OrderService", ["workspace"] = worktree });

        Assert.DoesNotContain("AmbiguousWorkspace", found, StringComparison.Ordinal);
        Assert.Contains("OrderService", found, StringComparison.Ordinal);
        Assert.Contains("workspace=FixtureSolution.slnx (" + worktree + ") - the worktree name matches 2 loaded solutions and this one sits at its root", found, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceStatus_WithNoHintAndTwoWorkspacesLoaded_AnswersOneLinePerWorkspaceInsteadOfRefusing()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: false, Token);

        await solution.CallAsync("load_workspace", new() { ["path"] = Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx") });

        var status = await solution.CallAsync("workspace_status", []);
        var verbose = await solution.CallAsync("workspace_status", new() { ["verbose"] = true });

        Assert.Contains("2 workspaces", status, StringComparison.Ordinal);
        Assert.Contains("FixtureSolution.slnx  worktree=" + Path.GetFileName(solution.Root) + " ", status, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("fixtures", "FixtureSolution", "FixtureSolution.slnx") + "  worktree=", status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pass workspace= for one workspace's full status", status, StringComparison.Ordinal);
        Assert.StartsWith("ERROR AmbiguousWorkspace", verbose, StringComparison.Ordinal);
    }

    private static Task WithNotesAsync(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "notes"));

        return File.WriteAllTextAsync(Path.Combine(root, "notes", "only-here.md"), "only here\n", Token);
    }

    private static Task WithNestedSolutionAsync(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "nested"));

        return File.WriteAllTextAsync(
            Path.Combine(root, "nested", "Nested.slnx"),
            "<Solution>\n  <Project Path=\"../src/Fixture.Trading/Fixture.Trading.csproj\" />\n</Solution>\n",
            Token);
    }
}
