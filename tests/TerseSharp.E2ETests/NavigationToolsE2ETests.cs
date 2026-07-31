namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class NavigationToolsE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task ToolsList_AdvertisesTheCoreSurface()
    {
        var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var names = tools.Select(tool => tool.Name).ToArray();

        Assert.Contains("load_workspace", names);
        Assert.Contains("get_file_outline", names);
        Assert.Contains("find_usages", names);
        Assert.Contains("rename_symbol", names);
        Assert.True(names.Length >= 20, string.Join(", ", names));
    }

    [Fact]
    public async Task LoadWorkspace_ReportsProjectsAndNoFailures()
    {
        var text = await server.CallAsync("load_workspace", new() { ["path"] = Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx") });

        Assert.Contains("projects=1", text, StringComparison.Ordinal);
        Assert.Contains("failures=0", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchSymbols_FindsTheTypeByName()
    {
        var text = await server.CallAsync("search_symbols", new() { ["query"] = "OrderService" });

        Assert.Contains("T:Fixture.Trading.OrderService", text, StringComparison.Ordinal);
        Assert.Contains("EXACT", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_ListsEveryMemberWithoutBodies()
    {
        var text = await server.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderService.cs" });

        Assert.Contains("T:Fixture.Trading.OrderService", text, StringComparison.Ordinal);
        Assert.Contains("M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)", text, StringComparison.Ordinal);
        Assert.Contains("P:Fixture.Trading.OrderService.PendingCount", text, StringComparison.Ordinal);
        Assert.DoesNotContain("repository.Submit(order)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_ListsEnumsAndDelegates()
    {
        var text = await server.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderSide.cs" });

        Assert.Contains("T:Fixture.Trading.OrderSide", text, StringComparison.Ordinal);
        Assert.Contains("F:Fixture.Trading.OrderSide.Buy", text, StringComparison.Ordinal);
        Assert.Contains("T:Fixture.Trading.OrderSubmitted", text, StringComparison.Ordinal);
        Assert.DoesNotContain("0 types", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSymbolSource_ReturnsOnlyThatMember()
    {
        var text = await server.CallAsync("get_symbol_source", new() { ["symbolId"] = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)" });

        Assert.Contains("public bool Submit(Order order) => repository.Submit(order);", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmitTwice", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindUsages_ReturnsSemanticReferencesOnly()
    {
        var text = await server.CallAsync("find_usages", new() { ["symbolId"] = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)" });

        Assert.Contains("OrderRouter.cs", text, StringComparison.Ordinal);
        Assert.Contains("OrderService.cs", text, StringComparison.Ordinal);
        Assert.Contains("truncated=false, total=4", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindImplementations_ResolvesThroughTheInterface()
    {
        var text = await server.CallAsync("find_implementations", new() { ["symbolId"] = "M:Fixture.Trading.IOrderRepository.Submit(Fixture.Trading.Order)" });

        Assert.Contains("InMemoryOrderRepository", text, StringComparison.Ordinal);
        Assert.Contains("NullOrderRepository", text, StringComparison.Ordinal);
        Assert.Contains("truncated=false, total=2", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindImplementations_WithMaxResultsOne_TruncatesButStillCountsThemAll()
    {
        var text = await server.CallAsync("find_implementations", new()
        {
            ["symbolId"] = "M:Fixture.Trading.IOrderRepository.Submit(Fixture.Trading.Order)",
            ["maxResults"] = 1,
        });

        Assert.Contains("1 implementations (truncated=true, total=2)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSymbol_DescribesKindAndLocation()
    {
        var text = await server.CallAsync("get_symbol", new() { ["symbolId"] = "T:Fixture.Trading.OrderService" });

        Assert.Contains("class public", text, StringComparison.Ordinal);
        Assert.Contains("OrderService.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDiagnostics_OnACleanFixture_ReportsNone()
    {
        var text = await server.CallAsync("get_diagnostics", new() { ["minSeverity"] = "error" });

        Assert.Contains("0 diagnostics", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SymbolNotFound_ReturnsTheErrorCodeAndARemedy()
    {
        var text = await server.CallAsync("get_symbol", new() { ["symbolId"] = "M:Fixture.Trading.OrderService.NoSuchMethod" });

        Assert.Contains("ERROR SymbolNotFound", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListProjects_NamesTheFixtureProject()
    {
        var text = await server.CallAsync("list_projects", []);

        Assert.Contains("Fixture.Trading", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceStatus_ReportsCountsBranchAndNoFailures()
    {
        var text = await server.CallAsync("workspace_status", []);

        Assert.Contains("projects=1", text, StringComparison.Ordinal);
        Assert.Contains("branch=", text, StringComparison.Ordinal);
        Assert.Contains("loadMs=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("FAILED", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListWorkspaces_ReportsBranchAndWorktree()
    {
        var text = await server.CallAsync("list_workspaces", []);

        Assert.Contains("worktree=", text, StringComparison.Ordinal);
        Assert.Contains("branch=", text, StringComparison.Ordinal);
    }
}
