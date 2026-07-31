namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class TokenBudgetE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task GetFileOutline_CostsAFractionOfReadingTheFile()
    {
        var outline = await server.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderBook.cs" });
        var whole = await File.ReadAllTextAsync(
            Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "OrderBook.cs"),
            TestContext.Current.CancellationToken);

        Assert.True(Tokens(outline) * 2 < Tokens(whole), Report("get_file_outline", outline, whole));
    }

    [Fact]
    public async Task ExploreSymbol_OnTheWidestSymbol_StaysUnderItsBudget()
    {
        var text = await server.CallAsync("explore_symbol", new() { ["symbolId"] = "T:Fixture.Trading.Order" });

        Assert.True(Tokens(text) <= 400, Report("explore_symbol", text));
    }

    [Fact]
    public async Task ImpactOf_OnTheWidestSymbol_StaysUnderItsBudget()
    {
        var text = await server.CallAsync("impact_of", new() { ["symbolId"] = "T:Fixture.Trading.Order" });

        Assert.True(Tokens(text) <= 400, Report("impact_of", text));
    }

    [Fact]
    public async Task XamlStyles_StaysUnderItsBudget()
    {
        var text = await server.CallAsync("xaml_styles", new() { ["typeName"] = "Button" });

        Assert.True(Tokens(text) <= 200, Report("xaml_styles", text));
    }

    [Fact]
    public async Task FindUsages_OnTheWidestSymbol_StaysUnderItsBudget()
    {
        var text = await server.CallAsync("find_usages", new() { ["symbolId"] = "T:Fixture.Trading.Order" });

        Assert.True(Tokens(text) <= 200, Report("find_usages", text));
    }

    [Fact]
    public async Task FindUsages_WithContainers_CostsMoreThanWithout_AndIsNotTheDefault()
    {
        var lean = await server.CallAsync("find_usages", new() { ["symbolId"] = "T:Fixture.Trading.Order" });

        var full = await server.CallAsync("find_usages", new()
        {
            ["symbolId"] = "T:Fixture.Trading.Order",
            ["containers"] = true,
        });

        Assert.True(Tokens(lean) < Tokens(full), Report("find_usages", lean, full));
    }

    [Fact]
    public async Task FindUsages_StaysUnderItsBudget()
    {
        var text = await server.CallAsync("find_usages", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)",
        });

        Assert.True(Tokens(text) <= 500, Report("find_usages", text));
    }

    [Fact]
    public async Task GetSymbol_StaysUnderItsBudget()
    {
        var text = await server.CallAsync("get_symbol", new() { ["symbolId"] = "T:Fixture.Trading.OrderService" });

        Assert.True(Tokens(text) <= 150, Report("get_symbol", text));
    }

    [Fact]
    public async Task Build_ReportsDiagnosticsNotBuildLogs()
    {
        var text = await server.CallAsync("build", []);

        Assert.True(Tokens(text) <= 700, Report("build", text));
    }

    [Fact]
    public async Task XamlOutline_CostsFarLessThanTheMarkup()
    {
        var outline = await server.CallAsync("xaml_outline", new() { ["path"] = "src/Fixture.Trading/Views/OrderView.xaml" });
        var whole = await File.ReadAllTextAsync(
            Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "Views", "OrderView.xaml"),
            TestContext.Current.CancellationToken);

        Assert.True(Tokens(outline) < Tokens(whole), Report("xaml_outline", outline, whole));
    }

    [Fact]
    public async Task EveryReadToolStaysWithinTheGlobalCap()
    {
        var responses = new[]
        {
            await server.CallAsync("workspace_status", []),
            await server.CallAsync("list_projects", []),
            await server.CallAsync("search_symbols", new() { ["query"] = "Order" }),
            await server.CallAsync("get_type_outline", new() { ["symbolId"] = "T:Fixture.Trading.OrderService" }),
        };

        Assert.All(responses, response => Assert.True(Tokens(response) <= 800, Report("read tool", response)));
    }

    private static int Tokens(string text) => (text.Length + 3) / 4;

    private static string Report(string tool, string response) =>
        string.Create(CultureInfo.InvariantCulture, $"{tool}: {Tokens(response)} tokens\n{response}");

    private static string Report(string tool, string response, string baseline) => string.Create(
        CultureInfo.InvariantCulture,
        $"{tool}: {Tokens(response)} tokens vs {Tokens(baseline)} for the raw file");
}
