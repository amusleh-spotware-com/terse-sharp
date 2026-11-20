namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class SessionReportE2ETests
{
    private static readonly string FixtureRoot = Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "FixtureSolution");

    [Fact]
    public async Task GetSymbolSource_WithItsFileOnAFreshServer_AnswersFromSyntaxWithoutRealizingACompilation()
    {
        var fresh = await StartedAsync();

        try
        {
            var text = await fresh.CallAsync("get_symbol_source", new() { ["symbolId"] = "OrderService.Submit", ["path"] = "src/Fixture.Trading/OrderService.cs" }, TestContext.Current.CancellationToken);

            Assert.Contains("OrderService.cs:11-11", text, StringComparison.Ordinal);
            Assert.Contains("public bool Submit(Order order)", text, StringComparison.Ordinal);
            Assert.DoesNotContain("compilations=realized", text, StringComparison.Ordinal);
        }
        finally
        {
            await fresh.StopAsync();
        }
    }

    [Fact]
    public async Task GetSymbolSource_WithItsFileForATypeName_StillAnswersTheSemanticOutline()
    {
        var fresh = await StartedAsync();

        try
        {
            var text = await fresh.CallAsync("get_symbol_source", new() { ["symbolId"] = "OrderService", ["path"] = "src/Fixture.Trading/OrderService.cs" }, TestContext.Current.CancellationToken);

            Assert.Contains("OrderService.Submit", text, StringComparison.Ordinal);
            Assert.Contains("compilations=realized", text, StringComparison.Ordinal);
        }
        finally
        {
            await fresh.StopAsync();
        }
    }

    private static Task<TerseServerProcess> StartedAsync() => TerseServerProcess.StartAsync(
        FixtureRoot,
        [TerseServerFixture.ServerAssemblyPath(), "serve", "--workspace", Path.Combine(FixtureRoot, "FixtureSolution.slnx")],
        TestContext.Current.CancellationToken);
}
