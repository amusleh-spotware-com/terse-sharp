namespace TerseSharp.E2ETests;

public sealed class StageE2ETests : IAsyncLifetime
{
    private readonly string root = Directory.CreateTempSubdirectory("terse-e2e-stage-").FullName;

    private TerseServerProcess server = null!;

    private string Solution => Path.Combine(root, "sub");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Solution);
        await File.WriteAllTextAsync(Path.Combine(Solution, "Probe.slnx"), "<Solution>\n  <Project Path=\"Probe.csproj\" />\n</Solution>\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Solution, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Solution, "notes.txt"), "a\n", TestContext.Current.CancellationToken);
        await GitAsync("init", "--quiet", "--initial-branch=main");
        await GitAsync("config", "core.autocrlf", "true");
        await GitAsync("add", "-A");
        await GitAsync("commit", "--no-verify", "--quiet", "-m", "initial");

        server = await TerseServerProcess.StartAsync(
            Solution,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(Solution, "Probe.slnx")],
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
    public async Task WriteText_WithStage_WritesTheIndexEntryOfThePathInsideTheSolutionAndLeavesTheWorkingTreeAlone()
    {
        var text = await CallAsync("write_text", new() { ["path"] = "notes.txt", ["content"] = "﻿b", ["stage"] = true });
        var size = await GitAsync("cat-file", "-s", ":sub/notes.txt");

        Assert.StartsWith("staged  notes.txt  changedLines=", text, StringComparison.Ordinal);
        Assert.Equal("4", size.Trim());
        Assert.Equal(string.Empty, await GitAsync("ls-files", "--stage", "--", "notes.txt"));
        Assert.Equal("a\n", await File.ReadAllTextAsync(Path.Combine(Solution, "notes.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteText_WithStage_StagesTheContentAsGitAddWouldUnderTheRepositoriesEolSettings()
    {
        await CallAsync("write_text", new() { ["path"] = "crlf.txt", ["content"] = "x\r\ny\r\n", ["stage"] = true });

        Assert.Equal("4", (await GitAsync("cat-file", "-s", ":sub/crlf.txt")).Trim());
    }

    [Fact]
    public async Task WriteText_WithStageAndRefHead_ResetsTheIndexEntryItStaged()
    {
        await CallAsync("write_text", new() { ["path"] = "notes.txt", ["content"] = "changed\n", ["stage"] = true });

        var text = await CallAsync("write_text", new() { ["path"] = "notes.txt", ["ref"] = "HEAD", ["stage"] = true });

        Assert.StartsWith("unstaged  notes.txt", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, await GitAsync("diff", "--cached", "--name-only"));
    }

    [Fact]
    public async Task WriteText_WithStageOnAPathNotOnDisk_AddsItToTheIndexOnlyAndRefHeadRemovesItAgain()
    {
        await CallAsync("write_text", new() { ["path"] = "added.txt", ["content"] = "x\n", ["stage"] = true });

        var staged = await GitAsync("ls-files", "--stage", "--", "sub/added.txt");

        await CallAsync("write_text", new() { ["path"] = "added.txt", ["ref"] = "HEAD", ["stage"] = true });

        Assert.Contains("sub/added.txt", staged, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Solution, "added.txt")));
        Assert.Equal(string.Empty, await GitAsync("ls-files", "--stage", "--", "sub/added.txt", "added.txt"));
    }

    [Fact]
    public async Task WriteText_WithStageOnAPathCarryingAGlob_StagesThatLiteralPathOnly()
    {
        await CallAsync("write_text", new() { ["path"] = "notes.txt", ["content"] = "changed\n", ["stage"] = true });
        await CallAsync("write_text", new() { ["path"] = "n*.txt", ["content"] = "glob\n", ["stage"] = true });
        await CallAsync("write_text", new() { ["path"] = "n*.txt", ["ref"] = "HEAD", ["stage"] = true });

        Assert.Equal("sub/notes.txt", (await GitAsync("diff", "--cached", "--name-only")).Trim());
    }

    [Fact]
    public async Task History_WithACommitOtherThanHead_AnswersOnlyThatCommit()
    {
        await File.WriteAllTextAsync(Path.Combine(Solution, "notes.txt"), "second\n", TestContext.Current.CancellationToken);
        await GitAsync("commit", "--no-verify", "--quiet", "-am", "second");

        var shas = (await GitAsync("rev-parse", "HEAD", "HEAD~1")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var text = await CallAsync("history", new() { ["commit"] = "HEAD~1" });

        Assert.Contains(shas[1][..7], text, StringComparison.Ordinal);
        Assert.DoesNotContain(shas[0][..7], text, StringComparison.Ordinal);
        Assert.DoesNotContain("second", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_WithStageBesideDelete_IsRefusedNamingWhatStageTakes()
    {
        var text = await CallAsync("write_text", new() { ["path"] = "notes.txt", ["delete"] = true, ["stage"] = true });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("stage=true writes ONE path's content= to the index", text, StringComparison.Ordinal);
        Assert.Equal("a\n", await File.ReadAllTextAsync(Path.Combine(Solution, "notes.txt"), TestContext.Current.CancellationToken));
    }

    private Task<string> GitAsync(params string[] arguments) => GitToolsE2ETests.GitOutputAsync(
        root,
        ["-c", "user.email=terse@example.com", "-c", "user.name=terse", "-c", "commit.gpgsign=false", .. arguments]);

    private Task<string> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);
}
