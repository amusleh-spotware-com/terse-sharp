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

        Assert.Contains("OrderService  class", text, StringComparison.Ordinal);
        Assert.Contains("  OrderService.Submit  ", text, StringComparison.Ordinal);
        Assert.Contains("OrderService.PendingCount", text, StringComparison.Ordinal);
        Assert.DoesNotContain("M:Fixture.Trading.OrderService.Submit", text, StringComparison.Ordinal);
        Assert.DoesNotContain("repository.Submit(order)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_KeepsTheDocumentationIdForAMemberAShortNameCannotAddress()
    {
        var text = await server.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/Awkward.cs" });

        Assert.Contains("M:Fixture.Trading.Awkward.#ctor(System.Int32)", text, StringComparison.Ordinal);
        Assert.Contains("M:Fixture.Trading.Awkward.Echo", text, StringComparison.Ordinal);
        Assert.Contains("M:Fixture.Trading.Awkward.op_Addition", text, StringComparison.Ordinal);
        Assert.Contains("  Awkward.Ordinary  ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_KeepsTheParameterListWhenTheOtherPartOfThePartialTypeOverloadsTheName()
    {
        var first = await server.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/SplitHandler.cs" });
        var second = await server.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/SplitHandler.Part.cs" });

        Assert.Contains("  SplitHandler.Route(int)  ", first, StringComparison.Ordinal);
        Assert.Contains("  SplitHandler.Dispatch  ", first, StringComparison.Ordinal);
        Assert.Contains("  SplitHandler.Route(string)  ", second, StringComparison.Ordinal);

        var resolved = await server.CallAsync("get_symbol", new() { ["symbolId"] = "SplitHandler.Route(string)" });

        Assert.Contains("M:Fixture.Trading.SplitHandler.Route(System.String)", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSymbolSource_OnAMemberWhosePayloadLooksLikeAConfidenceTag_ReturnsItByteForByte()
    {
        var text = await server.CallAsync("get_symbol_source", new() { ["symbolId"] = "SplitHandler.SampleTag" });

        Assert.Contains("SampleTag = \"  EXACT  \"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_ListsEnumsAndDelegates()
    {
        var text = await server.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderSide.cs" });

        Assert.Contains("OrderSide  enum", text, StringComparison.Ordinal);
        Assert.Contains("OrderSide.Buy", text, StringComparison.Ordinal);
        Assert.Contains("OrderSubmitted  delegate", text, StringComparison.Ordinal);
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
        Assert.StartsWith("4 usages in ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindUsages_NamesTheMemberEachUsageSitsIn()
    {
        var text = await server.CallAsync("find_usages", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)",
            ["containers"] = true,
        });

        Assert.Contains("in OrderRouter.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("in -", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindUsages_WithoutContainers_StaysOneLinePerFile()
    {
        var text = await server.CallAsync("find_usages", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)",
        });

        Assert.DoesNotContain("  in ", text, StringComparison.Ordinal);
        Assert.Equal(2, text.Split('\n').Skip(1).Count(line => line.Contains(".cs  ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task FindUsages_MarksEachCallSiteAsProductionOrTestCode()
    {
        var text = await server.CallAsync("find_usages", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)",
        });

        Assert.Contains("  src  ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("  test  ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryReferenceAnOutlinePrints_ResolvesBackToASymbol()
    {
        foreach (var file in new[] { "OrderService.cs", "Awkward.cs", "OrderBook.cs", "Reconciler.cs" })
        {
            var outline = await server.CallAsync("get_file_outline", new()
            {
                ["path"] = "src/Fixture.Trading/" + file,
            });

            foreach (var reference in References(outline))
            {
                var symbol = await server.CallAsync("get_symbol", new() { ["symbolId"] = reference });

                Assert.DoesNotContain("ERROR", symbol, StringComparison.Ordinal);
            }
        }
    }

    private static IEnumerable<string> References(string outline) => outline
        .Split('\n')
        .Skip(1)
        .Where(line => line.Trim().Length > 0)
        .Select(line => line.Trim().Split("  ", StringSplitOptions.RemoveEmptyEntries)[0]);

    [Fact]
    public async Task TheReferencesAnOutlinePrints_ResolveWhenFedStraightBackToAnotherTool()
    {
        var outline = await server.CallAsync("get_file_outline", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
        });

        var reference = outline
            .Split('\n')
            .First(line => line.Contains("OrderService.Submit  ", StringComparison.Ordinal))
            .Trim()
            .Split("  ", StringSplitOptions.RemoveEmptyEntries)[0];

        var symbol = await server.CallAsync("get_symbol", new() { ["symbolId"] = reference });

        Assert.DoesNotContain("ERROR", symbol, StringComparison.Ordinal);
        Assert.Contains("M:Fixture.Trading.OrderService.Submit", symbol, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_WithFullIds_StillEmitsDocumentationIds()
    {
        var text = await server.CallAsync("get_file_outline", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["ids"] = "full",
        });

        Assert.Contains("M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASymbolCanBeAddressedByNameInsteadOfItsDocumentationId()
    {
        var text = await server.CallAsync("get_symbol", new() { ["symbolId"] = "OrderService.Submit" });

        Assert.Contains("Fixture.Trading", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANameThatMatchesSeveralSymbols_ListsThemInsteadOfGuessing()
    {
        var text = await server.CallAsync("get_symbol", new() { ["symbolId"] = "Submit" });

        Assert.Contains("ERROR AmbiguousSymbol", text, StringComparison.Ordinal);
        Assert.Contains("M:Fixture.Trading.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANameWithAParameterCount_PicksTheRightOverload()
    {
        var text = await server.CallAsync("get_symbol", new() { ["symbolId"] = "Reconciler.Reconcile(Order)" });

        Assert.Contains("M:Fixture.Trading.Reconciler.Reconcile(Fixture.Trading.Order)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANameWithATwoParameterCount_PicksTheOtherOverload()
    {
        var text = await server.CallAsync("get_symbol", new()
        {
            ["symbolId"] = "Reconciler.Reconcile(Order, decimal)",
        });

        Assert.Contains("System.Decimal", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGenericArgumentComma_DoesNotInflateTheParameterCount()
    {
        var text = await server.CallAsync("get_symbol", new()
        {
            ["symbolId"] = "Reconciler.Reconcile(Dictionary<string,int>, Order)",
        });

        Assert.Contains("System.Collections.Generic.Dictionary", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOverloadedNameWithNoParameterCount_ListsTheCandidatesAndDeclaresTheTotal()
    {
        var text = await server.CallAsync("get_symbol", new() { ["symbolId"] = "Reconciler.Reconcile" });

        Assert.Contains("resolves to 3 symbols", text, StringComparison.Ordinal);
        Assert.Contains("showing 3 of 3", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFullyQualifiedNameResolvesTheSameAsAShortOne()
    {
        var text = await server.CallAsync("get_symbol", new()
        {
            ["symbolId"] = "Fixture.Trading.OrderService.Submit",
        });

        Assert.Contains("M:Fixture.Trading.OrderService.Submit", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANameThatMatchesNothing_NamesTheNearestSymbols()
    {
        var text = await server.CallAsync("get_symbol", new() { ["symbolId"] = "OrderService.Submitt" });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindImplementations_ResolvesThroughTheInterface()
    {
        var text = await server.CallAsync("find_implementations", new() { ["symbolId"] = "M:Fixture.Trading.IOrderRepository.Submit(Fixture.Trading.Order)" });

        Assert.Contains("InMemoryOrderRepository", text, StringComparison.Ordinal);
        Assert.Contains("NullOrderRepository", text, StringComparison.Ordinal);
        Assert.StartsWith("2 implementations", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindImplementations_WithMaxResultsOne_TruncatesButStillCountsThemAll()
    {
        var text = await server.CallAsync("find_implementations", new()
        {
            ["symbolId"] = "M:Fixture.Trading.IOrderRepository.Submit(Fixture.Trading.Order)",
            ["maxResults"] = 1,
        });

        Assert.StartsWith("1/2 implementations truncated", text, StringComparison.Ordinal);
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
        Assert.Contains("documents=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("loadMs=", text, StringComparison.Ordinal);
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
