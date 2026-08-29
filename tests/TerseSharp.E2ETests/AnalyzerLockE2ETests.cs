using System.Diagnostics;

namespace TerseSharp.E2ETests;

public sealed class AnalyzerLockE2ETests : IAsyncLifetime
{
    private TerseServerProcess server = null!;

    public static string FixtureRoot { get; } =
        Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "GeneratorSolution");

    public static string SolutionPath { get; } = Path.Combine(FixtureRoot, "GeneratorSolution.slnx");

    private static string AnalyzerAssembly { get; } = Path.Combine(
        FixtureRoot,
        "src",
        "Fixture.Generator",
        "bin",
        "Debug",
        "netstandard2.0",
        "Fixture.Generator.dll");

    private static string ConsumerPath { get; } =
        Path.Combine(FixtureRoot, "src", "Fixture.Generated", "Consumer.cs");

    public async ValueTask InitializeAsync()
    {
        await EnsureBuiltAsync();

        server = await TerseServerProcess.StartAsync(
            FixtureRoot,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--workspace", SolutionPath],
            TestContext.Current.CancellationToken);
    }

    private static async Task EnsureBuiltAsync()
    {
        var output = string.Empty;

        for (var attempt = 0; attempt < 3 && !File.Exists(AnalyzerAssembly); attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            output = (await RebuildAsync()).Output;
        }

        Assert.True(File.Exists(AnalyzerAssembly), output);
    }

    public async ValueTask DisposeAsync() => await server.StopAsync();

    [Fact]
    public async Task SemanticTools_OnASolutionBuildingItsOwnAnalyzer_LeaveTheAnalyzerAssemblyWritable()
    {
        await ArmedAsync();

        Assert.Contains(
            "Describe",
            await CallAsync("get_symbol", new() { ["symbolId"] = "Consumer.Describe" }),
            StringComparison.Ordinal);

        Assert.True(Writable(), "get_symbol mapped the analyzer assembly in place");

        Assert.Contains("FIX001", await AnalyzeAsync(), StringComparison.Ordinal);
        Assert.True(Writable(), "analyze mapped the analyzer assembly in place");
    }

    [Fact]
    public async Task ExternalBuild_WhileTheServerHasRunTheAnalyzer_Succeeds()
    {
        await ArmedAsync();

        Assert.Contains("FIX001", await AnalyzeAsync(), StringComparison.Ordinal);

        File.SetLastWriteTimeUtc(GeneratorSource, DateTime.UtcNow);

        var (exitCode, output) = await RebuildAsync();

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("MSB302", output, StringComparison.Ordinal);
    }
    [Fact]
    public async Task UnloadWorkspace_AfterTheAnalyzerRan_ReportsNothingStillMapped()
    {
        await ArmedAsync();

        Assert.Contains("FIX001", await AnalyzeAsync(), StringComparison.Ordinal);

        var unloaded = await CallAsync("unload_workspace", new() { ["path"] = SolutionPath });

        Assert.Contains("unloaded", unloaded, StringComparison.Ordinal);
        Assert.False(unloaded.Contains("still mapped into this server process", StringComparison.Ordinal), unloaded);
        Assert.DoesNotContain("Fixture.Generator.dll", unloaded, StringComparison.Ordinal);
        Assert.True(Writable(), "the analyzer assembly stayed mapped after unload_workspace");
    }

    [Fact]
    public async Task EditThenUndo_OnTheGeneratorConsumingProject_RoundTripsAndLeavesTheAnalyzerWritable()
    {
        var original = await File.ReadAllBytesAsync(ConsumerPath, TestContext.Current.CancellationToken);

        try
        {
            await ArmedAsync();

            Assert.Contains("FIX001", await AnalyzeAsync(), StringComparison.Ordinal);

            var applied = await CallAsync("replace_symbol_body", new()
            {
                ["symbolId"] = "Consumer.Describe",
                ["body"] = "=> Greeting.Text + \"!\"",
            });

            Assert.Contains("changedLines=", applied, StringComparison.Ordinal);
            Assert.Contains("Greeting.Text + \"!\"", await CurrentConsumerAsync(), StringComparison.Ordinal);
            Assert.True(Writable(), "the edit mapped the analyzer assembly in place");

            Assert.Contains("reverted", await CallAsync("undo_last_change", []), StringComparison.Ordinal);
            Assert.Equal(original, await File.ReadAllBytesAsync(ConsumerPath, TestContext.Current.CancellationToken));
            Assert.True(Writable(), "the undo mapped the analyzer assembly in place");
        }
        finally
        {
            await File.WriteAllBytesAsync(ConsumerPath, original, TestContext.Current.CancellationToken);
        }
    }
    private async Task ArmedAsync()
    {
        await CallAsync("build", []);

        Assert.True(File.Exists(AnalyzerAssembly), AnalyzerAssembly);
        Assert.True(Writable(), "the analyzer assembly was already locked before any semantic call");
    }

    private Task<string> AnalyzeAsync() => CallAsync("analyze", new() { ["minSeverity"] = "info" });

    private static Task<string> CurrentConsumerAsync() =>
        File.ReadAllTextAsync(ConsumerPath, TestContext.Current.CancellationToken);

    private static async Task<(int ExitCode, string Output)> RebuildAsync()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = FixtureRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add("build");
        start.ArgumentList.Add(SolutionPath);
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        return await ReadAsync(start);
    }

    private static async Task<(int ExitCode, string Output)> ReadAsync(ProcessStartInfo start)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");

        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return (process.ExitCode, await output + await error);
    }

    private static bool Writable()
    {
        try
        {
            using var stream = File.Open(AnalyzerAssembly, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private Task<string> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);

    private static string GeneratorSource { get; } =
        Path.Combine(FixtureRoot, "src", "Fixture.Generator", "GreetingGenerator.cs");

    [Fact]
    public async Task ALockedOutput_WithASecondWorkspaceLoaded_IsStillRetriedAgainstTheWorkspaceTheCallResolvedTo()
    {
        await ArmedAsync();

        var second = Path.Combine(FixtureRoot, "src", "Fixture.Generator", "Fixture.Generator.csproj");
        var loaded = await CallAsync("load_workspace", new() { ["path"] = second });

        Assert.DoesNotContain("ERROR", loaded, StringComparison.Ordinal);
        Assert.Contains("Fixture.Generator", await CallAsync("list_workspaces", []), StringComparison.Ordinal);

        try
        {
            File.SetLastWriteTimeUtc(GeneratorSource, DateTime.UtcNow);

            using var held = File.Open(AnalyzerAssembly, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var text = await CallAsync("build", new() { ["workspace"] = "GeneratorSolution" });

            Assert.DoesNotContain("was not retried", text, StringComparison.Ordinal);
            Assert.Contains("the workspace was unloaded and the build retried", text, StringComparison.Ordinal);
        }
        finally
        {
            await CallAsync("unload_workspace", new() { ["path"] = second });
        }
    }
}
