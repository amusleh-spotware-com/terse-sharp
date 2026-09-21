namespace TerseSharp.E2ETests;

public sealed class AnalyzerGapE2ETests : IAsyncLifetime
{
    private static readonly string GapRoot =
        Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "AnalyzerGapSolution");

    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync() =>
        server = await TerseServerProcess.StartAsync(
            GapRoot,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(GapRoot, "AnalyzerGapSolution.slnx")],
            TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => server.StopAsync();

    [Fact]
    public async Task FindUsages_WithAnUnresolvedAnalyzerReference_AnswersTheUsagesInsteadOfThrowing()
    {
        var text = await CallAsync("find_usages", new() { ["symbol"] = "GapService.Update" });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("GapService.cs", text, StringComparison.Ordinal);
        Assert.StartsWith("2 usages in 1 files", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindUsages_WithImpact_AnswersTheBlastRadiusInsteadOfThrowing()
    {
        var text = await CallAsync("find_usages", new() { ["symbol"] = "GapService.Update", ["impact"] = true });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("Gap.Core", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindImplementations_WithAnUnresolvedAnalyzerReference_NamesTheDerivedType()
    {
        var text = await CallAsync("find_implementations", new() { ["symbol"] = "GapBase" });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("GapService", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceStatus_WithAnUnresolvedAnalyzerReference_SaysHowManyWereDropped()
    {
        var text = await CallAsync("workspace_status", []);

        Assert.Contains("analyzers=1 unresolved in 1 project(s)", text, StringComparison.Ordinal);
        Assert.Contains("Gap.Analyzer.NotOnDisk.dll", text, StringComparison.Ordinal);
        Assert.DoesNotContain(GapRoot, text, StringComparison.Ordinal);
    }

    private Task<string> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);
}
