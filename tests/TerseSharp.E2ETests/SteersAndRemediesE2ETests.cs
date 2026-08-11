namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class SteersAndRemediesE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task WorkspaceStatus_ReportsTheRunningVersion()
    {
        var text = await server.CallAsync("workspace_status", []);

        Assert.Contains("terse=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("terse=unknown", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_OnAPathNamedAfterAType_NamesTheFileThatDeclaresIt()
    {
        var text = await server.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/ServiceBag.cs" });

        Assert.Contains("'ServiceBag' is declared in", text, StringComparison.Ordinal);
        Assert.Contains("Composition.cs", text, StringComparison.Ordinal);
        Assert.DoesNotContain("use find_files to locate it", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_OnAFileThatDoesNotExistYet_NamesWriteText()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["path"] = "src/Fixture.Trading/NeverWritten.cs",
            ["declaration"] = "public sealed record Unwritten(int Value);",
        });

        Assert.Contains("write_text path=src/Fixture.Trading/NeverWritten.cs force=true", text, StringComparison.Ordinal);
        Assert.DoesNotContain("use find_files to locate it", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSymbolSource_OnATypeId_AnswersTheOutlineAndSteersToOneMember()
    {
        var text = await server.CallAsync("get_symbol_source", new() { ["symbolIds"] = new[] { "OrderRouter" } });

        Assert.Contains("OrderRouter.Route", text, StringComparison.Ordinal);
        Assert.Contains("steer: get_symbol_source symbolId=OrderRouter.Member", text, StringComparison.Ordinal);
        Assert.DoesNotContain("this.service = service", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSymbolSource_OnATypeIdWithVerbose_StillAnswersTheSource()
    {
        var text = await server.CallAsync("get_symbol_source", new() { ["symbolId"] = "OrderRouter", ["verbose"] = true });

        Assert.Contains("this.service = service", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SolutionProjects_ReadsASolutionFilter()
    {
        var filter = Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnf");
        var text = await server.CallAsync("solution_projects", new() { ["path"] = filter });

        Assert.Contains("1 projects", text, StringComparison.Ordinal);
        Assert.Contains("Fixture.Trading.csproj", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clean_WithAPath_SweepsASolutionThatIsNotLoaded()
    {
        var solution = Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx");
        var text = await server.CallAsync("clean", new() { ["path"] = solution, ["dryRun"] = true });

        Assert.Contains("dryRun", text, StringComparison.Ordinal);
        Assert.Contains("directories", text, StringComparison.Ordinal);
        Assert.Contains("freedBytes=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("files=0 ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clean_WithAPathThatIsNotASolution_IsRefusedByName()
    {
        var readme = Path.Combine(TerseServerFixture.RepositoryRoot, "README.md");
        var text = await server.CallAsync("clean", new() { ["path"] = readme, ["dryRun"] = true });

        Assert.Contains("is not a solution or project file", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_WithAMultilineEntry_StillOffersEveryLineToTheOtherEntries()
    {
        var text = await server.CallAsync("search_text", new()
        {
            ["queries"] = new[] { "class OrderRouter\n{", "{" },
            ["glob"] = "**/OrderRouter.cs",
        });

        Assert.Contains("q1", text, StringComparison.Ordinal);
        Assert.Contains("q2", text, StringComparison.Ordinal);
        Assert.Contains("2 matches", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithUsings_AddsTheImportInTheSameEdit()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "OrderRouter",
            ["declaration"] = "public string Describe() => new StringBuilder(\"routed\").ToString();",
            ["usings"] = new[] { "System.Text" },
            ["dryRun"] = true,
            ["verbose"] = true,
        });

        Assert.Contains("+using System.Text;", text, StringComparison.Ordinal);
        Assert.Contains("Describe()", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchSymbols_KeepsTheProductionHalfAndNamesTheTestHalf()
    {
        var solution = Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "SelectionSolution", "SelectionSolution.slnx");

        await server.CallAsync("load_workspace", new() { ["path"] = solution });

        try
        {
            var text = await server.CallAsync("search_symbols", new() { ["query"] = "Add", ["workspace"] = "SelectionSolution" });

            Assert.Contains("more in test projects - scope=test", text, StringComparison.Ordinal);
            Assert.DoesNotContain("AdderTests", text, StringComparison.Ordinal);
            Assert.Contains("Adder", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("unload_workspace", new() { ["path"] = solution });
        }
    }

    [Fact]
    public async Task SolutionProjects_ResolvesAFilterAgainstTheSolutionItPointsAt()
    {
        var filter = Path.Combine(TerseServerFixture.FixtureRoot, "filters", "Nested.slnf");
        var text = await server.CallAsync("solution_projects", new() { ["path"] = filter });

        Assert.Contains("../src/Fixture.Trading/Fixture.Trading.csproj", text, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Fixture.Trading", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbol_BatchedWithUsings_AddsTheImportEvenWhenADeclarationIsUnchanged()
    {
        var text = await server.CallAsync("replace_symbol", new()
        {
            ["symbolIds"] = new[] { "OrderRouter.Route", "OrderRouter.Retry" },
            ["declarations"] = new[]
            {
            "public bool Route(Order order) => service.Submit(order);",
            "public bool Retry(Order order) => service.Submit(order);",
        },
            ["usings"] = new[] { "System.Text" },
            ["dryRun"] = true,
            ["verbose"] = true,
        });

        Assert.Contains("+using System.Text;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("no change", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithAUsingThatIsNotANamespace_IsRefusedByName()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "OrderRouter",
            ["declaration"] = "public int Answer() => 42;",
            ["usings"] = new[] { " " },
            ["dryRun"] = true,
        });

        Assert.Contains("is not a namespace", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }
}
