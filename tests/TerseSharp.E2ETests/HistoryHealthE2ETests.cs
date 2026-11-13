namespace TerseSharp.E2ETests;

public sealed class HistoryHealthE2ETests : IAsyncLifetime
{
    private readonly string root = Directory.CreateTempSubdirectory("terse-e2e-history-").FullName;

    private readonly string home = Directory.CreateTempSubdirectory("terse-e2e-history-home-").FullName;

    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(home, ".terse.json"), """{"policy":{}}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "Probe.slnx"), "<Solution>\n  <Project Path=\"Probe.csproj\" />\n</Solution>\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net11.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "Lonely.cs"), "namespace Probe;\n\npublic sealed class Lonely;\n", TestContext.Current.CancellationToken);
        await GitAsync("alice", "init", "--quiet", "--initial-branch=main");

        foreach (var author in new[] { "alice", "bob", "carol", "dave", "erin" })
            await CommitAsync(author);

        server = await TerseServerProcess.StartAsync(
            root,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(root, "Probe.slnx")],
            new Dictionary<string, string> { ["TERSE_UPDATE"] = "0", ["TERSE_HOME"] = home },
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await server.StopAsync();

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        TerseTempSolution.Delete(root);
        Directory.Delete(home, recursive: true);
    }

    [Fact]
    public async Task Analyze_ForAFileFiveAuthorsChangedTogetherWithAnother_ReportsDeveloperCongestionAndChangeCouplingAsHeuristicCodeHealthLines()
    {
        var text = await CallAsync("analyze", new() { ["path"] = "Congested.cs", ["ids"] = "TERSE114" });

        Assert.Contains("Congested.cs:1:1: <file> - developer congestion HEURISTIC: 5 authors in the last 200 commits exceeds 3", text, StringComparison.Ordinal);
        Assert.Contains("Congested.cs:1:1: <file> - change coupling HEURISTIC: co-changed with Partner.cs in 5 of its 5 commits reaches 50%", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_ForAFileOneAuthorCommittedOnce_ReportsNoHistorySmell()
    {
        var text = await CallAsync("analyze", new() { ["path"] = "Lonely.cs", ["ids"] = "TERSE114" });

        Assert.Contains("engines=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.DoesNotContain("HEURISTIC", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_OnACongestedFile_LandsWithNoHistorySmellBecauseTheEditGateNeverReadsHistory()
    {
        var analyzed = await CallAsync("analyze", new() { ["path"] = "Partner.cs", ["ids"] = "TERSE114" });
        var applied = await CallAsync("write_text", new() { ["path"] = "Partner.cs", ["content"] = "namespace Probe;\n\npublic sealed class Partner\n{\n    public string Author => \"frank\";\n}\n", ["force"] = true });

        Assert.Contains("developer congestion HEURISTIC: 5 authors", analyzed, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", applied, StringComparison.Ordinal);
        Assert.DoesNotContain("congestion", applied, StringComparison.Ordinal);
        Assert.DoesNotContain("coupling", applied, StringComparison.Ordinal);
    }

    private async Task CommitAsync(string author)
    {
        await File.WriteAllTextAsync(Path.Combine(root, "Congested.cs"), "namespace Probe;\n\npublic sealed class Congested\n{\n    public string Author => \"" + author + "\";\n}\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "Partner.cs"), "namespace Probe;\n\npublic sealed class Partner\n{\n    public string Author => \"" + author + "\";\n}\n", TestContext.Current.CancellationToken);
        await GitAsync(author, "add", "-A");
        await GitAsync(author, "commit", "--no-verify", "--quiet", "-m", author);
    }

    private Task<string> GitAsync(string author, params string[] arguments) => GitToolsE2ETests.GitOutputAsync(
        root,
        ["-c", "user.email=" + author + "@example.com", "-c", "user.name=" + author, "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false", .. arguments]);

    private Task<string> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);
}
