namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class NewToolEdgeCaseE2ETests(TerseServerFixture server)
{
    private const string View = "src/Fixture.Trading/Views/OrderView.xaml";
    private const string Nested = "src/Fixture.Trading/Views/Nested.xaml";

    [Theory]
    [InlineData("explore_symbol")]
    [InlineData("impact_of")]
    public async Task ASymbolToolOnAnUnknownSymbol_FailsWithARemedy(string tool)
    {
        var text = await server.CallAsync(tool, new() { ["symbolId"] = "M:No.Such.Thing" });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("explore_symbol")]
    [InlineData("impact_of")]
    public async Task ASymbolToolOnAnAmbiguousName_ListsCandidates(string tool)
    {
        var text = await server.CallAsync(tool, new() { ["symbolId"] = "Submit" });

        Assert.Contains("ERROR AmbiguousSymbol", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("explore_symbol")]
    [InlineData("impact_of")]
    public async Task ASymbolToolWithAnEmptySymbol_IsRefused(string tool)
    {
        var text = await server.CallAsync(tool, new() { ["symbolId"] = "" });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExploreSymbol_OnAType_Works()
    {
        var text = await server.CallAsync("explore_symbol", new() { ["symbolId"] = "T:Fixture.Trading.Order" });

        Assert.Contains("usages=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindRegistrations_WithAnEmptyQuery_ReturnsEveryRegistrationRatherThanFailing()
    {
        var text = await server.CallAsync("find_registrations", new() { ["query"] = "" });

        Assert.Contains("registrations", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindRegistrations_RespectsMaxResults()
    {
        var text = await server.CallAsync("find_registrations", new() { ["query"] = "", ["maxResults"] = 1 });

        Assert.Contains(" truncated", text, StringComparison.Ordinal);
        Assert.Contains("narrow with", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListEndpoints_RespectsMaxResults()
    {
        var text = await server.CallAsync("list_endpoints", new() { ["maxResults"] = 1 });

        Assert.Contains(" truncated", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlAddElement_InsertsAfterTheExistingChildrenNotBeforeThem()
    {
        var text = await server.CallAsync("xaml_add_element", new()
        {
            ["path"] = Nested,
            ["target"] = "#Outer",
            ["markup"] = "<TextBlock Text=\"Added\" />",
            ["dryRun"] = true,
        });

        var added = text.IndexOf("Text=\"Added\"", StringComparison.Ordinal);
        var trailing = text.IndexOf("x:Name=\"Trailing\"", StringComparison.Ordinal);

        Assert.True(added > trailing, "the new child must come after the existing ones: " + text);
    }

    [Fact]
    public async Task XamlRemoveElement_RemovesAnElementThatNestsAnotherOfTheSameType()
    {
        var text = await server.CallAsync("xaml_remove_element", new()
        {
            ["path"] = Nested,
            ["target"] = "#Outer",
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("-  <Grid x:Name=\"Outer\">", text, StringComparison.Ordinal);
        Assert.Contains("-    <Grid x:Name=\"Inner\">", text, StringComparison.Ordinal);
        Assert.Contains("-    <Button x:Name=\"Trailing\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlAddElement_OnASelfClosingElement_IsRefusedWithAReason()
    {
        var text = await server.CallAsync("xaml_add_element", new()
        {
            ["path"] = View,
            ["target"] = "#SymbolText",
            ["markup"] = "<Run Text=\"x\" />",
            ["dryRun"] = true,
        });

        Assert.Contains("self-closing", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlAddElement_WithMalformedMarkup_IsRefused()
    {
        var text = await server.CallAsync("xaml_add_element", new()
        {
            ["path"] = View,
            ["target"] = "Window/Grid",
            ["markup"] = "<Broken",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("malformed XAML", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlRemoveElement_RemovesTheWholeElement()
    {
        var text = await server.CallAsync("xaml_remove_element", new()
        {
            ["path"] = View,
            ["target"] = "#VolumeText",
            ["dryRun"] = true,
        });

        Assert.Contains("-    <TextBlock x:Name=\"VolumeText\"", text, StringComparison.Ordinal);
        Assert.Contains("dryRun", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlRemoveElement_OnAnAmbiguousTarget_ListsTheProblemRatherThanGuessing()
    {
        var text = await server.CallAsync("xaml_remove_element", new()
        {
            ["path"] = View,
            ["target"] = "#SubmitButton",
            ["dryRun"] = true,
        });

        Assert.Contains("matched 2 elements", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlRemoveElement_OnAMissingTarget_SaysSo()
    {
        var text = await server.CallAsync("xaml_remove_element", new()
        {
            ["path"] = View,
            ["target"] = "key=NoSuchKey",
            ["dryRun"] = true,
        });

        Assert.Contains("matched no element", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlValidate_WithIncludeUnused_ReportsADeclarationNothingReferences()
    {
        var text = await server.CallAsync("xaml_validate", new()
        {
            ["scope"] = "solution",
            ["includeUnused"] = true,
            ["maxResults"] = 200,
        });

        Assert.Contains("XAML004", text, StringComparison.Ordinal);
        Assert.Contains("HEURISTIC", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlValidate_WithoutIncludeUnused_ReportsNoDeadDeclarations()
    {
        var text = await server.CallAsync("xaml_validate", new() { ["scope"] = "solution" });

        Assert.DoesNotContain("XAML004", text, StringComparison.Ordinal);
        Assert.DoesNotContain("XAML005", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_WithAnUnknownIdsValue_IsRefusedRatherThanSilentlyDefaulted()
    {
        var text = await server.CallAsync("get_file_outline", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["ids"] = "fulll",
        });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_WithSinceLast_ReportsNothingNewOnASecondIdenticalRun()
    {
        await server.CallAsync("analyze", new() { ["path"] = "src/Fixture.Trading/OrderService.cs" });

        var text = await server.CallAsync("analyze", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["sinceLast"] = true,
        });

        Assert.Contains("new diagnostics", text, StringComparison.Ordinal);
        Assert.Contains("appeared=0", text, StringComparison.Ordinal);
    }
}
