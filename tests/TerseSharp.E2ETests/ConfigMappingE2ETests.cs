namespace TerseSharp.E2ETests;

[Collection(nameof(ConfigMappingSolutionCollection))]
public sealed class ConfigMappingE2ETests : IAsyncLifetime
{
    private static readonly string Root =
        Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "ConfigMappingSolution");

    private static readonly string Solution = Path.Combine(Root, "ConfigMappingSolution.slnx");

    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync()
    {
        server = await TerseServerProcess.StartAsync(
            Root,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Solution],
            TestContext.Current.CancellationToken);

        await server.CallAsync("build", [], TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await server.StopAsync();

    [Fact]
    public async Task LoadWorkspace_WhenTheSolutionBuildsAProjectAtAnotherConfiguration_WarnsNamingThatProjectOnly()
    {
        var text = await Reloaded();

        Assert.Contains(
            "WARNING config-mapping src/Mapped/Mapped.csproj builds as Release|x64 under Debug|Any CPU, loaded as Debug|AnyCPU",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Plain.csproj", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDiagnosticsAndEdits_OverAProjectLoadedAtAnotherConfiguration_ReportTheSeverityTheSolutionBuildsItWith()
    {
        await Reloaded();

        var text = await server.CallAsync(
            "get_diagnostics",
            new() { ["path"] = "src/Mapped/Unawaited.cs" },
            TestContext.Current.CancellationToken);
        var edit = await server.CallAsync(
            "edit_text",
            new() { ["path"] = "src/Mapped/Unawaited.cs", ["oldText"] = "await Task.Yield();", ["newText"] = "await Task.Yield();\n        await Task.Yield();", ["dryRun"] = true },
            TestContext.Current.CancellationToken);

        Assert.Contains("CS4014 warning", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CS4014 error", text, StringComparison.Ordinal);
        Assert.Contains(
            "config-mapping 1 project(s) load at a different Configuration|Platform than the solution builds them - their warnaserror and nowarn severities were re-read at the solution's configuration",
            text,
            StringComparison.Ordinal);
        Assert.Contains("errors=0", edit, StringComparison.Ordinal);
    }

    private Task<string> Reloaded() => server.CallAsync(
        "load_workspace",
        new() { ["path"] = Solution, ["reload"] = true },
        TestContext.Current.CancellationToken);
}
