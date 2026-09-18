namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class ExploreToolsE2ETests(TerseServerFixture server)
{
    private const string Submit = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)";

    [Fact]
    public async Task GetSymbol_WithUsages_AnswersSignatureLocationAndReachInOneCall()
    {
        var text = await server.CallAsync("get_symbol", new() { ["symbolId"] = Submit, ["usages"] = true });

        Assert.Contains("method public", text, StringComparison.Ordinal);
        Assert.Contains("usages=4", text, StringComparison.Ordinal);
        Assert.Contains("OrderRouter.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSymbol_WithUsages_SeparatesTestUsagesFromProductionOnes()
    {
        var text = await server.CallAsync("get_symbol", new() { ["symbolId"] = Submit, ["usages"] = true });

        Assert.Contains("(test=0)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSymbol_WithUsagesAndABatch_StillDescribesEveryIdRatherThanExploringOne()
    {
        var text = await server.CallAsync("get_symbol", new()
        {
            ["symbolIds"] = new[] { Submit, "T:Fixture.Trading.Order" },
            ["usages"] = true,
        });

        Assert.Contains("2 symbols", text, StringComparison.Ordinal);
        Assert.DoesNotContain("usages=", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindUsages_WithImpact_NamesEveryProjectThatWouldRecompile()
    {
        var text = await server.CallAsync("find_usages", new() { ["symbolId"] = Submit, ["impact"] = true });

        Assert.Contains("projects that would recompile: 1", text, StringComparison.Ordinal);
        Assert.Contains("Fixture.Trading", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindUsages_WithImpact_OnABoundProperty_IncludesTheXamlSites()
    {
        var text = await server.CallAsync("find_usages", new() { ["symbolId"] = "OrderViewModel.Symbol", ["impact"] = true });

        Assert.Contains("xaml binding", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindRegistrations_FindsTheContainerCallsForAType()
    {
        var text = await server.CallAsync("find_registrations", new() { ["query"] = "IOrderRepository" });

        Assert.Contains("AddSingleton", text, StringComparison.Ordinal);
        Assert.Contains("AddScoped", text, StringComparison.Ordinal);
        Assert.Contains("in Composition.Register", text, StringComparison.Ordinal);
        Assert.Contains("2 registrations", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindRegistrations_WhenNothingMatches_SaysSoInsteadOfImplyingItIsUnregistered()
    {
        var text = await server.CallAsync("find_registrations", new() { ["query"] = "INoSuchService" });

        Assert.Contains("0 registrations", text, StringComparison.Ordinal);
        Assert.Contains("assembly scanning", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListEndpoints_ReportsEveryMapCallWithItsMember()
    {
        var text = await server.CallAsync("list_endpoints", []);

        Assert.Contains("MapGet", text, StringComparison.Ordinal);
        Assert.Contains("MapPost", text, StringComparison.Ordinal);
        Assert.Contains("in Composition.Routes", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindUsages_WithImpactAndTests_OverASolutionWithNoTestProject_SaysSoRatherThanAnsweringNothing()
    {
        var without = await server.CallAsync("find_usages", new() { ["symbolId"] = "OrderService.Submit", ["impact"] = true });
        var with = await server.CallAsync("find_usages", new() { ["symbolId"] = "OrderService.Submit", ["impact"] = true, ["tests"] = true });

        Assert.DoesNotContain("no test declaration references this symbol directly", without, StringComparison.Ordinal);
        Assert.Contains("no test declaration references this symbol directly", with, StringComparison.Ordinal);
        Assert.Contains("run the whole suite", with, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindUsages_WithImpactAndTests_ForASymbolNoTestNames_SaysSoRatherThanAnsweringNothing()
    {
        var text = await server.CallAsync("find_usages", new() { ["symbolId"] = "T:Fixture.Trading.Awkward", ["impact"] = true, ["tests"] = true });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("test", text, StringComparison.Ordinal);
    }
}
