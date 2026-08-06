
namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class BacklogClosureE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task ReadText_WithTail_ReturnsTheLastLinesOnly()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderSide.cs",
            ["tail"] = 2,
        });

        Assert.Contains("OrderSubmitted", text, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace Fixture.Trading", text, StringComparison.Ordinal);
        Assert.DoesNotContain("public enum OrderSide", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WhenTheToolClipsTheRead_NamesTheLineToContinueFrom()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderBook.cs",
            ["maxLines"] = 3,
        });

        Assert.Contains("next: startLine=5 (total=", text, StringComparison.Ordinal);
        Assert.Contains("outline: get_file_outline path=src/Fixture.Trading/OrderBook.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithACallerChosenRange_AddsNoContinuationSteer()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "appsettings.json",
            ["startLine"] = 2,
            ["endLine"] = 3,
        });

        Assert.DoesNotContain("next: startLine=", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_WithContext_ReturnsTheSurroundingLinesIndented()
    {
        var bare = await server.CallAsync("search_text", new() { ["query"] = "class OrderBook", ["glob"] = "**/*.cs" });
        var withContext = await server.CallAsync("search_text", new()
        {
            ["query"] = "class OrderBook",
            ["glob"] = "**/*.cs",
            ["context"] = 2,
        });

        Assert.True(withContext.Split('\n').Length > bare.Split('\n').Length, withContext);
        Assert.Contains("\n    ", withContext, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_WithoutContext_IsByteIdenticalToTheOldAnswer()
    {
        var bare = await server.CallAsync("search_text", new() { ["query"] = "OrderBook", ["glob"] = "**/*.cs" });
        var explicitZero = await server.CallAsync("search_text", new()
        {
            ["query"] = "OrderBook",
            ["glob"] = "**/*.cs",
            ["context"] = 0,
        });

        Assert.Equal(bare, explicitZero);
    }

    [Fact]
    public async Task SearchText_WithUnique_CollapsesRepeatedLinesIntoOneRecord()
    {
        var every = await server.CallAsync("search_text", new() { ["query"] = "namespace Fixture", ["glob"] = "**/*.cs" });
        var collapsed = await server.CallAsync("search_text", new()
        {
            ["query"] = "namespace Fixture",
            ["glob"] = "**/*.cs",
            ["unique"] = true,
        });

        var everyRecords = every.Split('\n').Count(line => line.Contains(".cs:", StringComparison.Ordinal));
        var collapsedRecords = collapsed.Split('\n').Count(line => line.Contains(".cs:", StringComparison.Ordinal));

        Assert.True(everyRecords > collapsedRecords, $"{everyRecords} -> {collapsedRecords}\n{collapsed}");
        Assert.Contains("  x", collapsed, StringComparison.Ordinal);
        Assert.Contains("unique:", collapsed, StringComparison.Ordinal);
        Assert.DoesNotContain("truncated", collapsed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSymbolSource_WithSeveralIds_AnswersOnceAndNamesTheIdItCouldNotResolve()
    {
        var text = await server.CallAsync("get_symbol_source", new()
        {
            ["symbolIds"] = new[] { "OrderBook.Add", "Fixture.Trading.NoSuchMember" },
        });

        Assert.Contains("2 symbols", text, StringComparison.Ordinal);
        Assert.Contains("NOT_RESOLVED Fixture.Trading.NoSuchMember", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EverySymbolTool_AcceptsTheSymbolAliasInsteadOfSymbolId()
    {
        var source = await server.CallAsync("get_symbol_source", new() { ["symbol"] = "OrderBook.Add" });
        var usages = await server.CallAsync("find_usages", new() { ["symbol"] = "T:Fixture.Trading.OrderBook" });

        Assert.DoesNotContain("ERROR", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", usages, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSymbolSource_WithNeitherIdNorAlias_NamesTheMissingArgument()
    {
        var text = await server.CallAsync("get_symbol_source", []);

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("symbolId", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_OnAnEnum_AddsTheEnumMember()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderSide",
            ["declaration"] = "Hold",
            ["dryRun"] = true,
            ["verbose"] = true,
        });

        Assert.Contains("Hold", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not a type declaration", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbol_OnAnEnumMember_RewritesThatMember()
    {
        var text = await server.CallAsync("replace_symbol", new()
        {
            ["symbolId"] = "F:Fixture.Trading.OrderSide.Sell",
            ["declaration"] = "Sell = 7",
            ["dryRun"] = true,
            ["verbose"] = true,
        });

        Assert.Contains("Sell = 7", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithAFilePath_AppendsANamespaceLevelType()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["path"] = "src/Fixture.Trading/OrderSide.cs",
            ["declaration"] = "public sealed record OrderTag(string Value);",
            ["dryRun"] = true,
            ["verbose"] = true,
        });

        Assert.Contains("OrderTag", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_WithDelete_RemovesAFileAndIsRefusedOnCSharpWithoutForce()
    {
        var refused = await server.CallAsync("write_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderSide.cs",
            ["delete"] = true,
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR", refused, StringComparison.Ordinal);

        var missing = await server.CallAsync("write_text", new()
        {
            ["path"] = "terse-no-such-file.txt",
            ["delete"] = true,
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR DocumentNotFound", missing, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListProjects_PrintsThePathBesideEveryProject()
    {
        var text = await server.CallAsync("list_projects", []);

        Assert.Contains("Fixture.Trading", text, StringComparison.Ordinal);
        Assert.Contains(".csproj", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceStatus_ReportsMappedAnalyzersWithoutUnloading()
    {
        var text = await server.CallAsync("workspace_status", new() { ["verbose"] = true });

        Assert.Contains("mapped=", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAndRunTests_AdvertiseConfigurationAndTargetFramework()
    {
        var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        foreach (var name in new[] { "build", "run_tests", "rerun_failed", "list_tests" })
        {
            var schema = tools.Single(tool => tool.Name == name).JsonSchema.GetProperty("properties");

            Assert.True(schema.TryGetProperty("configuration", out _), name + " has no configuration");
            Assert.True(schema.TryGetProperty("targetFramework", out _), name + " has no targetFramework");
        }
    }

    [Fact]
    public async Task SearchText_WithAnAbsoluteRoot_SearchesOutsideTheWorkspaceAndSaysSo()
    {
        var outside = Path.GetDirectoryName(TerseServerFixture.FixtureRoot)!;
        var text = await server.CallAsync("search_text", new()
        {
            ["query"] = "FixtureSolution",
            ["root"] = outside,
            ["glob"] = "**/*.slnx",
        });

        Assert.Contains("outside-workspace", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_WithARelativeRoot_IsRefusedWithARemedy()
    {
        var text = await server.CallAsync("search_text", new()
        {
            ["query"] = "anything",
            ["root"] = "../not-absolute",
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_WithARootThatDoesNotExist_SaysSoInsteadOfAnsweringZero()
    {
        var text = await server.CallAsync("search_text", new()
        {
            ["query"] = "anything",
            ["root"] = Path.Combine(Path.GetTempPath(), "terse-no-such-directory-9d2f"),
        });

        Assert.Contains("ERROR DocumentNotFound", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithMaxChars_BoundsAFileWhoseLinesAreTooLongForMaxLines()
    {
        var bounded = await server.CallAsync("read_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderBook.cs",
            ["maxChars"] = 120,
        });
        var whole = await server.CallAsync("read_text", new() { ["path"] = "src/Fixture.Trading/OrderBook.cs" });

        Assert.True(bounded.Length < whole.Length, bounded);
        Assert.Contains("next: startLine=", bounded, StringComparison.Ordinal);
        Assert.DoesNotContain("next: startLine=", whole, StringComparison.Ordinal);
    }
}
