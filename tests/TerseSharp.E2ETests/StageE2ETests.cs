namespace TerseSharp.E2ETests;

public sealed class StageE2ETests : IAsyncLifetime
{
    private readonly string root = Directory.CreateTempSubdirectory("terse-e2e-stage-").FullName;

    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(root, "Probe.slnx"), "<Solution>\n  <Project Path=\"Probe.csproj\" />\n</Solution>\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "notes.txt"), "a\n", TestContext.Current.CancellationToken);
        await GitAsync("init", "--quiet", "--initial-branch=main");
        await GitAsync("add", "-A");
        await GitAsync("commit", "--no-verify", "--quiet", "-m", "initial");

        server = await TerseServerProcess.StartAsync(
            root,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(root, "Probe.slnx")],
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
    public async Task WriteText_WithStage_WritesTheIndexByteForByteAndLeavesTheWorkingTreeAlone()
    {
        var text = await CallAsync("write_text", new() { ["path"] = "notes.txt", ["content"] = "﻿b", ["stage"] = true });
        var size = await GitAsync("cat-file", "-s", ":notes.txt");

        Assert.StartsWith("staged  notes.txt  changedLines=", text, StringComparison.Ordinal);
        Assert.Equal("4", size.Trim());
        Assert.Equal("a\n", await File.ReadAllTextAsync(Path.Combine(root, "notes.txt"), TestContext.Current.CancellationToken));
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

        var staged = await GitAsync("ls-files", "--stage", "--", "added.txt");

        await CallAsync("write_text", new() { ["path"] = "added.txt", ["ref"] = "HEAD", ["stage"] = true });

        Assert.Contains("added.txt", staged, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "added.txt")));
        Assert.Equal(string.Empty, await GitAsync("ls-files", "--stage", "--", "added.txt"));
    }

    [Fact]
    public async Task WriteText_WithStageBesideDelete_IsRefusedNamingWhatStageTakes()
    {
        var text = await CallAsync("write_text", new() { ["path"] = "notes.txt", ["delete"] = true, ["stage"] = true });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("stage=true writes ONE path's content= to the index", text, StringComparison.Ordinal);
        Assert.Equal("a\n", await File.ReadAllTextAsync(Path.Combine(root, "notes.txt"), TestContext.Current.CancellationToken));
    }

    private Task<string> GitAsync(params string[] arguments) => GitToolsE2ETests.GitOutputAsync(
        root,
        ["-c", "user.email=terse@example.com", "-c", "user.name=terse", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false", .. arguments]);

    private Task<string> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);
}
