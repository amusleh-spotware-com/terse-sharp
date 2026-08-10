namespace TerseSharp.E2ETests;

public sealed class LoadFailureE2ETests : IAsyncLifetime
{
    private static readonly string UnloadableRoot =
        Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "UnloadableSolution");

    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync() =>
        server = await TerseServerProcess.StartAsync(
            UnloadableRoot,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(UnloadableRoot, "UnloadableSolution.slnx")],
            TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => server.StopAsync();

    [Fact]
    public async Task WorkspaceStatus_Verbose_NamesTheFailedProjectRelativeToTheWorkspaceRoot()
    {
        var text = await CallAsync("workspace_status", new() { ["verbose"] = true });
        var failed = Failed(text);

        Assert.Contains("Project file not found:", failed, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("src", "Absent", "Absent.csproj"), failed, StringComparison.Ordinal);
        Assert.DoesNotContain(UnloadableRoot, failed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadWorkspace_Verbose_NamesTheFailedProjectRelativeToTheWorkspaceRoot()
    {
        var text = await CallAsync("load_workspace", new()
        {
            ["path"] = Path.Combine(UnloadableRoot, "UnloadableSolution.slnx"),
            ["verbose"] = true,
        });

        var failed = Failed(text);

        Assert.Contains("Project file not found:", failed, StringComparison.Ordinal);
        Assert.DoesNotContain(UnloadableRoot, failed, StringComparison.Ordinal);
    }

    private static string Failed(string response) =>
        response.Split('\n').Single(line => line.StartsWith("FAILED ", StringComparison.Ordinal));

    [Fact]
    public async Task WorkspaceStatus_WithoutVerbose_StillGroupsTheFailureByItsProjectFileName()
    {
        var text = await CallAsync("workspace_status", []);

        Assert.Contains("1 load failure(s) in 1 project(s)", text, StringComparison.Ordinal);
        Assert.Contains("FAILED Absent.csproj  messages=1", text, StringComparison.Ordinal);
    }

    private Task<string> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);
}
