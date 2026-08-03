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

    public async ValueTask InitializeAsync() => server = await TerseServerProcess.StartAsync(
        FixtureRoot,
        [TerseServerFixture.ServerAssemblyPath(), "serve", "--workspace", SolutionPath],
        TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await server.StopAsync();

    [Fact]
    public async Task UnloadWorkspace_WhenTheWorkspaceLoadedAnAnalyzer_ReportsThatItStaysMappedInsteadOfClaimingTheLocksAreGone()
    {
        await CallAsync("build", []);

        Assert.True(File.Exists(AnalyzerAssembly), AnalyzerAssembly);
        Assert.True(Writable(), "the analyzer assembly was already locked before any semantic call");

        Assert.Contains("FIX001", await CallAsync("analyze", new() { ["minSeverity"] = "info" }), StringComparison.Ordinal);

        Assert.False(Writable(), "loading the analyzer no longer maps it; the unload_workspace warning is now a false positive");

        var unloaded = await CallAsync("unload_workspace", new() { ["path"] = SolutionPath });

        Assert.Contains("unloaded", unloaded, StringComparison.Ordinal);
        Assert.Contains("still mapped into this server process", unloaded, StringComparison.Ordinal);
        Assert.Contains("MSB3027", unloaded, StringComparison.Ordinal);
        Assert.Contains("Fixture.Generator.dll", unloaded, StringComparison.Ordinal);
        Assert.False(Writable(), "unload_workspace released the analyzer assembly, so the warning is wrong");
    }

    private static bool Writable()
    {
        try
        {
            using var stream = File.Open(AnalyzerAssembly, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private Task<string> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);
}
