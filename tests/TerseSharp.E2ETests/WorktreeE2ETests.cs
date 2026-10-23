namespace TerseSharp.E2ETests;

public sealed class WorktreeE2ETests : IAsyncLifetime
{
    private readonly string root = Directory.CreateTempSubdirectory("terse-e2e-worktree-").FullName;

    private TerseServerProcess server = null!;

    private string Main => Path.Combine(root, "main");

    private string Linked => Path.Combine(Main, ".wt", "linked");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Main);
        await File.WriteAllTextAsync(Path.Combine(Main, "Probe.slnx"), "<Solution>\n  <Project Path=\"Probe.csproj\" />\n</Solution>\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Main, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Main, "notes.txt"), "a\n", TestContext.Current.CancellationToken);
        await GitAsync(Main, "init", "--quiet", "--initial-branch=main");
        await GitAsync(Main, "add", "-A");
        await GitAsync(Main, "commit", "--no-verify", "--quiet", "-m", "initial");
        await GitAsync(Main, "worktree", "add", "--quiet", "-b", "linked", Linked);

        server = await TerseServerProcess.StartAsync(
            Linked,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(Linked, "Probe.slnx")],
            new Dictionary<string, string> { ["TERSE_UPDATE"] = "0", ["TERSE_HOME"] = root },
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await server.StopAsync();

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ChangedFiles_InALinkedWorktreeRepeatedWithNoEventInBetween_ReplaysTheListingAsUnchanged()
    {
        await File.WriteAllTextAsync(Path.Combine(Linked, "notes.txt"), "b\n", TestContext.Current.CancellationToken);

        var arguments = new Dictionary<string, object?> { ["path"] = "notes.txt" };
        var first = await CallAsync(arguments);
        var second = await SettledAsync(arguments);

        Assert.Contains("notes.txt", first, StringComparison.Ordinal);
        Assert.DoesNotContain("UNCHANGED", first, StringComparison.Ordinal);
        Assert.Contains("UNCHANGED", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangedFiles_InALinkedWorktree_IsNotReplayedAfterItsBranchMovesInTheCommonDirectory()
    {
        var arguments = new Dictionary<string, object?> { ["path"] = "notes.txt" };
        var settled = await SettledAsync(arguments);

        await File.WriteAllTextAsync(Path.Combine(Main, "notes.txt"), "moved\n", TestContext.Current.CancellationToken);
        await GitAsync(Main, "commit", "--no-verify", "--quiet", "-am", "moved");
        await GitAsync(Main, "update-ref", "refs/heads/linked", "main");

        var after = await CallAsync(arguments);

        Assert.Contains("UNCHANGED", settled, StringComparison.Ordinal);
        Assert.DoesNotContain("UNCHANGED", after, StringComparison.Ordinal);
        Assert.Contains("notes.txt", after, StringComparison.Ordinal);
    }

    private async Task<string> SettledAsync(Dictionary<string, object?> arguments)
    {
        var answer = await CallAsync(arguments);

        for (var attempt = 0; attempt < 50 && !answer.Contains("UNCHANGED", StringComparison.Ordinal); attempt++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            answer = await CallAsync(arguments);
        }

        return answer;
    }

    private Task<string> CallAsync(Dictionary<string, object?> arguments) =>
        server.CallAsync("changed_files", new(arguments), TestContext.Current.CancellationToken);

    private static Task<string> GitAsync(string directory, params string[] arguments) => GitToolsE2ETests.GitOutputAsync(
        directory,
        ["-c", "user.email=terse@example.com", "-c", "user.name=terse", "-c", "commit.gpgsign=false", .. arguments]);
}
