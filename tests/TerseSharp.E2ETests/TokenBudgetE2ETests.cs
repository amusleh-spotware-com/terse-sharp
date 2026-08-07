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
    public async Task SearchSymbols_WhenCapped_CostsLessThanTheUncappedAnswerButKeepsTheTotal()
    {
        var capped = await server.CallAsync("search_symbols", new() { ["query"] = "Order", ["maxResults"] = 2 });
        var full = await server.CallAsync("search_symbols", new() { ["query"] = "Order", ["maxResults"] = 200 });

        Assert.True(Tokens(capped) < Tokens(full), Report("search_symbols", capped, full));
        Assert.Contains(" truncated", capped, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_WithUsings_CostsOneExtraLine()
    {
        var bare = await server.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/Localization.cs" });
        var withUsings = await server.CallAsync("get_file_outline", new()
        {
            ["path"] = "src/Fixture.Trading/Localization.cs",
            ["usings"] = true,
        });

        Assert.Equal(Lines(bare) + 1, Lines(withUsings));
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
    public async Task ResxGet_KeysOnly_CostsAFractionOfReadingTheWidestResourceFile()
    {
        var keys = await server.CallAsync("resx_get", new()
        {
            ["path"] = "src/Fixture.Trading/Resources/Wide.resx",
            ["values"] = false,
            ["maxResults"] = 200,
        });

        var whole = await File.ReadAllTextAsync(
            Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "Resources", "Wide.resx"),
            TestContext.Current.CancellationToken);

        Assert.True(Tokens(keys) * 3 < Tokens(whole), Report("resx_get", keys, whole));
    }

    [Fact]
    public async Task ResxGet_WithAPrefix_StaysUnderItsBudget()
    {
        var text = await server.CallAsync("resx_get", new()
        {
            ["path"] = "src/Fixture.Trading/Strings.resx",
            ["cultures"] = "all",
            ["prefix"] = "Caption_",
        });

        Assert.True(Tokens(text) <= 250, Report("resx_get", text));
    }

    [Fact]
    public async Task ResxValidate_OnTheWholeWorkspace_StaysUnderItsBudget()
    {
        var text = await server.CallAsync("resx_validate", []);

        Assert.True(Tokens(text) <= 800, Report("resx_validate", text));
    }

    [Fact]
    public async Task ResxFiles_StaysUnderItsBudget()
    {
        var text = await server.CallAsync("resx_files", []);

        Assert.True(Tokens(text) <= 400, Report("resx_files", text));
    }

    [Fact]
    public async Task ResxUsages_StaysUnderItsBudget()
    {
        var text = await server.CallAsync("resx_usages", new() { ["key"] = "Caption_Submit" });

        Assert.True(Tokens(text) <= 250, Report("resx_usages", text));
    }

    [Fact]
    public async Task FormatVerify_OnACleanFile_CostsAVerdictNotADiff()
    {
        var verdict = await server.CallAsync("format", new()
        {
            ["path"] = "src/Fixture.Trading/Order.cs",
            ["verify"] = true,
        });

        var diff = await server.CallAsync("format", new()
        {
            ["path"] = "src/Fixture.Trading/Order.cs",
            ["dryRun"] = true,
        });

        Assert.True(Tokens(verdict) <= 60, Report("format verify", verdict));
        Assert.True(Tokens(verdict) <= Tokens(diff), Report("format verify", verdict, diff));
    }

    [Fact]
    public async Task CleanupVerify_OnTheWholeSolution_StaysUnderItsBudget()
    {
        var text = await server.CallAsync("cleanup", new() { ["verify"] = true });

        Assert.True(Tokens(text) <= 150, Report("cleanup verify", text));
    }

    [Fact]
    public async Task CleanDryRun_ReportsCountersNotAFileList()
    {
        var text = await server.CallAsync("clean", new() { ["dryRun"] = true });

        Assert.True(Tokens(text) <= 300, Report("clean", text));
        Assert.Contains("projects=", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_OnTheWidestFile_NumbersOnlyTheLinesThatBreakTheSequence()
    {
        var quiet = await server.CallAsync("read_text", new() { ["path"] = "src/Fixture.Trading/OrderBook.cs" });
        var loud = await server.CallAsync("read_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderBook.cs",
            ["verbose"] = true,
        });

        Assert.True(Gutters(quiet) * 2 < Gutters(loud), Report("read_text", quiet));
        Assert.True(Tokens(quiet) * 10 < Tokens(loud) * 9, Report("read_text", quiet, loud));
    }

    [Fact]
    public async Task GetSymbolSource_OnTheWidestMember_IsDedentedAndBlankFree()
    {
        var quiet = await server.CallAsync("get_symbol_source", new() { ["symbolId"] = "OrderBook.TotalVolume" });
        var loud = await server.CallAsync("get_symbol_source", new()
        {
            ["symbolId"] = "OrderBook.TotalVolume",
            ["verbose"] = true,
        });

        Assert.DoesNotContain("\n\n", quiet, StringComparison.Ordinal);
        Assert.Contains("\n{", quiet, StringComparison.Ordinal);
        Assert.Contains("\n    {", loud, StringComparison.Ordinal);
        Assert.True(Tokens(quiet) < Tokens(loud), Report("get_symbol_source", quiet, loud));
    }

    [Fact]
    public async Task EditText_OnSuccess_NamesTheFileWithoutItsDirectory()
    {
        var path = Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "budget-probe.json");

        await File.WriteAllTextAsync(path, "{ \"probe\": 1 }", TestContext.Current.CancellationToken);

        try
        {
            var text = await server.CallAsync("edit_text", new()
            {
                ["path"] = "src/Fixture.Trading/budget-probe.json",
                ["oldText"] = "1",
                ["newText"] = "2",
            });

            Assert.Equal("budget-probe.json  changedLines=1", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static int Tokens(string text) => ToolCensus.Tokens(text);
    private static int Lines(string text) => text.Split('\n').Length;

    private static int Gutters(string text) => text
        .Split('\n')
        .Count(line => line.Length > 0 && char.IsAsciiDigit(line[0]) && line.Contains(": ", StringComparison.Ordinal));

    private static string Report(string tool, string response) =>
        string.Create(CultureInfo.InvariantCulture, $"{tool}: {Tokens(response)} tokens\n{response}");

    private static string Report(string tool, string response, string baseline) => string.Create(
        CultureInfo.InvariantCulture,
        $"{tool}: {Tokens(response)} tokens vs {Tokens(baseline)} for the raw file");

    [Fact]
    public async Task WorkspaceStatus_KeepsItsTelemetryBehindVerbose()
    {
        var quiet = await server.CallAsync("workspace_status", []);
        var loud = await server.CallAsync("workspace_status", new() { ["verbose"] = true });

        Assert.Contains("documents=", quiet, StringComparison.Ordinal);
        Assert.DoesNotContain("gen=c", quiet, StringComparison.Ordinal);
        Assert.DoesNotContain("index=xaml(", quiet, StringComparison.Ordinal);
        Assert.DoesNotContain("lastUsedUtc=", quiet, StringComparison.Ordinal);
        Assert.Equal(3, quiet.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.True(Tokens(quiet) * 2 < Tokens(loud), Report("workspace_status", quiet, loud));
        Assert.Contains("watch=", loud, StringComparison.Ordinal);
        Assert.Contains("gen=c", loud, StringComparison.Ordinal);
        Assert.Contains("index=xaml(", loud, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameSymbol_OnTheWidestSymbol_AnswersWellUnderTheDiffItReplaces()
    {
        var quiet = await server.CallAsync("rename_symbol", new()
        {
            ["symbolId"] = "T:Fixture.Trading.Order",
            ["newName"] = "OrderBudgetProbe",
        });

        var loud = await server.CallAsync("rename_symbol", new()
        {
            ["symbolId"] = "T:Fixture.Trading.OrderBudgetProbe",
            ["newName"] = "Order",
            ["verbose"] = true,
        });

        Assert.DoesNotContain("@@", quiet, StringComparison.Ordinal);
        Assert.DoesNotContain(":\\", quiet, StringComparison.Ordinal);
        Assert.True(Tokens(quiet) <= 200, Report("rename_symbol", quiet));
        Assert.True(Tokens(quiet) * 3 < Tokens(loud), Report("rename_symbol", quiet, loud));
    }

    [Fact]
    public async Task ListProjects_WithAFilter_CostsLessThanTheUnfilteredListing()
    {
        var all = await server.CallAsync("list_projects", []);
        var filtered = await server.CallAsync("list_projects", new() { ["filter"] = "Nope" });

        Assert.True(Tokens(filtered) < Tokens(all), Report("list_projects", filtered, all));
        Assert.True(Lines(filtered) < Lines(all), Report("list_projects", filtered, all));
        Assert.Equal("0 projects", filtered);
    }

    [Fact]
    public async Task SearchText_WithoutContext_CostsNothingExtraAndWithContextStaysProportional()
    {
        var bare = await server.CallAsync("search_text", new() { ["query"] = "namespace Fixture", ["glob"] = "**/*.cs" });
        var withContext = await server.CallAsync("search_text", new()
        {
            ["query"] = "namespace Fixture",
            ["glob"] = "**/*.cs",
            ["context"] = 3,
        });

        Assert.True(Tokens(withContext) > Tokens(bare), Report("search_text context", withContext, bare));
        Assert.True(Tokens(withContext) < Tokens(bare) * 8, Report("search_text context", withContext, bare));
    }

    [Fact]
    public async Task GetSymbolSource_WithABatch_CostsLessThanTheSameMembersOneCallAtATime()
    {
        var ids = new[] { "OrderBook.Add", "OrderBook.Remove", "OrderBook.Total" };
        var batched = await server.CallAsync("get_symbol_source", new() { ["symbolIds"] = ids });
        var separate = 0;

        foreach (var id in ids)
            separate += Tokens(await server.CallAsync("get_symbol_source", new() { ["symbolId"] = id }));

        Assert.True(Tokens(batched) <= separate, $"batched={Tokens(batched)} separate={separate}\n{batched}");
    }

    [Fact]
    public async Task WorkspaceStatus_WithNoMappedAnalyzers_GainsNothingFromTheMappedCounter()
    {
        var status = await server.CallAsync("workspace_status", []);

        Assert.DoesNotContain("mapped=", status, StringComparison.Ordinal);
        Assert.True(Tokens(status) < 120, Report("workspace_status", status, status));
    }

    [Fact]
    public async Task AClippedRead_PaysAtMostTwoExtraLinesForTheContinuationSteer()
    {
        var clipped = await server.CallAsync("read_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderBook.cs",
            ["maxLines"] = 3,
        });
        var whole = await server.CallAsync("read_text", new() { ["path"] = "src/Fixture.Trading/OrderBook.cs" });

        Assert.Equal(2, Lines(clipped) - 3 - 1);
        Assert.DoesNotContain("next: startLine=", whole, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithMaxChars_StaysWithinTheBudgetItWasGiven()
    {
        var bounded = await server.CallAsync("read_text", new()
        {
            ["path"] = "wide-lines.json",
            ["maxChars"] = 500,
        });

        Assert.True(Tokens(bounded) < 300, Report("read_text maxChars", bounded));
        Assert.True(bounded.Length < 1200, $"{bounded.Length} characters for a 500-character budget");
    }
}
