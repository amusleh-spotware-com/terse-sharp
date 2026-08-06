namespace TerseSharp.E2ETests;

public sealed class RazorToolsE2ETests : IAsyncLifetime
{
    private const string Card = "src/Fixture.Blazor/Components/Card.razor";
    private const string Home = "src/Fixture.Blazor/Components/Home.razor";
    private const string Page = "src/Fixture.Blazor/Pages/Index.cshtml";

    private TerseServerProcess server = null!;

    public static string FixtureRoot { get; } =
        Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "RazorSolution");

    public static string SolutionPath { get; } = Path.Combine(FixtureRoot, "RazorSolution.slnx");

    public async ValueTask InitializeAsync() => server = await TerseServerProcess.StartAsync(
        FixtureRoot,
        [TerseServerFixture.ServerAssemblyPath(), "serve", "--workspace", SolutionPath],
        TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await server.StopAsync();

    [Fact]
    public async Task RazorOutline_ResolvesEveryComponentToItsType()
    {
        var text = await RazorAsync("razor_outline", new() { ["path"] = Card });

        Assert.Contains("kind=component", text, StringComparison.Ordinal);
        Assert.Contains("type=Fixture.Blazor.Components.Card", text, StringComparison.Ordinal);
        Assert.Contains("rendermode=InteractiveServer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("generator=unavailable", text, StringComparison.Ordinal);
        Assert.Contains("EXACT Fixture.Blazor.Components.Badge", text, StringComparison.Ordinal);
        Assert.Contains("@implements", text, StringComparison.Ordinal);
        Assert.DoesNotContain("_razor.g.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorOutline_ListsTheMembersDeclaredInCodeWithTheirRazorLines()
    {
        var text = await RazorAsync("razor_outline", new() { ["path"] = Card });

        Assert.Contains("Title", text, StringComparison.Ordinal);
        Assert.Contains("[Parameter, EditorRequired]", text, StringComparison.Ordinal);
        Assert.Contains("Toggle()", text, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildRenderTree", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorOutline_ReadsACshtmlPageAsAPage()
    {
        var text = await RazorAsync("razor_outline", new() { ["path"] = Page });

        Assert.Contains("kind=page", text, StringComparison.Ordinal);
        Assert.Contains("@model", text, StringComparison.Ordinal);
        Assert.Contains("asp-for", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorComponent_ReportsParametersAndWhichAreRequired()
    {
        var text = await RazorAsync("razor_component", new() { ["name"] = "Badge" });

        Assert.Contains("Fixture.Blazor.Components.Badge", text, StringComparison.Ordinal);
        Assert.Contains("Kind  string  [Parameter, EditorRequired]", text, StringComparison.Ordinal);
        Assert.Contains("ChildContent  RenderFragment", text, StringComparison.Ordinal);
        Assert.Contains("OnDismiss  EventCallback<MouseEventArgs>", text, StringComparison.Ordinal);
        Assert.Contains("Components/Badge.razor", text.Replace('\\', '/'), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorComponent_RefusesAnUnknownName()
    {
        var text = await RazorAsync("razor_component", new() { ["name"] = "NoSuchComponent" });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorFind_LocatesComponentUsages()
    {
        var text = await RazorAsync("razor_find", new() { ["query"] = "Card", ["kind"] = "component" });

        Assert.Contains("Components/Home.razor", text.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Contains("component", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorFind_LocatesRoutes()
    {
        var text = await RazorAsync("razor_find", new() { ["query"] = "order", ["kind"] = "route" });

        Assert.Contains("/order/{Id:int}", text, StringComparison.Ordinal);
        Assert.Contains("2 hits", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorBindings_ResolvesHandlersAndBindTargets()
    {
        var text = await RazorAsync("razor_bindings", new() { ["path"] = Card, ["validate"] = true });

        Assert.Contains("context=Fixture.Blazor.Components.Card", text, StringComparison.Ordinal);
        Assert.Contains("@onclick", text, StringComparison.Ordinal);
        Assert.Contains("EXACT", text, StringComparison.Ordinal);
        Assert.Contains("Filter", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorCodeBehind_LinksTheCollocatedFiles()
    {
        var text = (await RazorAsync("razor_codebehind", new() { ["path"] = Card })).Replace('\\', '/');

        Assert.Contains("codeBehind=src/Fixture.Blazor/Components/Card.razor.cs", text, StringComparison.Ordinal);
        Assert.Contains("scopedCss=src/Fixture.Blazor/Components/Card.razor.css", text, StringComparison.Ordinal);
        Assert.Contains("javaScript=-", text, StringComparison.Ordinal);
        Assert.Contains("imports=1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorValidate_ReportsTheFaultsTheCompilerDoesNotCatch()
    {
        var text = await RazorAsync("razor_validate", new() { ["scope"] = "solution" });

        Assert.Contains("RZR002", text, StringComparison.Ordinal);
        Assert.Contains("Card.Bogus", text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException at render", text, StringComparison.Ordinal);
        Assert.Contains("RZR001", text, StringComparison.Ordinal);
        Assert.Contains("MudButton", text, StringComparison.Ordinal);
        Assert.Contains("RZR003", text, StringComparison.Ordinal);
        Assert.Contains("RZR006", text, StringComparison.Ordinal);
        Assert.Contains("AmbiguousMatchException", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorValidate_NarrowsToTheRulesAsked()
    {
        var text = await RazorAsync("razor_validate", new() { ["scope"] = "solution", ["rules"] = "RZR006" });

        Assert.Contains("RZR006", text, StringComparison.Ordinal);
        Assert.DoesNotContain("RZR002", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorSetAttribute_PreviewsTheDiffWithoutWriting()
    {
        var text = await RazorAsync("razor_set_attribute", new()
        {
            ["path"] = Card,
            ["target"] = "div/Badge",
            ["attribute"] = "Count",
            ["value"] = "0",
            ["dryRun"] = true,
        });

        Assert.Contains("dryRun", text, StringComparison.Ordinal);
        Assert.Contains("+    <Badge Kind=\"warning\" Count=\"0\" />", text, StringComparison.Ordinal);
        Assert.Contains("changedLines=1", text, StringComparison.Ordinal);
        Assert.Contains("errors=0 (+0)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorSetAttribute_RollsBackAnEditThatBreaksTheBuild()
    {
        var text = await RazorAsync("razor_set_attribute", new()
        {
            ["path"] = Card,
            ["target"] = "div/Badge",
            ["attribute"] = "Count",
            ["value"] = "@NoSuchMember",
        });

        Assert.Contains("ERROR CompileRegression", text, StringComparison.Ordinal);
        Assert.Contains("Card.razor", text, StringComparison.Ordinal);
        Assert.DoesNotContain("_razor.g.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorSetAttribute_RefusesAnAmbiguousTarget()
    {
        var text = await RazorAsync("razor_set_attribute", new()
        {
            ["path"] = Card,
            ["target"] = "nothing-like-this",
            ["attribute"] = "Count",
            ["value"] = "1",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("razor_outline", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorAddElement_InsertsInsideTheAddressedElement()
    {
        var text = await RazorAsync("razor_add_element", new()
        {
            ["path"] = Card,
            ["parent"] = "div",
            ["markup"] = "<Badge Kind=\"info\" />",
            ["dryRun"] = true,
        });

        Assert.Contains("+    <Badge Kind=\"info\" />", text, StringComparison.Ordinal);
        Assert.Contains("errors=0 (+0)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorRemoveElement_CutsTheElementAndItsChildren()
    {
        var text = await RazorAsync("razor_remove_element", new()
        {
            ["path"] = Card,
            ["target"] = "div/button",
            ["dryRun"] = true,
            ["allowErrors"] = true,
        });

        Assert.Contains("-    <button class=\"btn\" @onclick=\"Toggle\">Toggle</button>", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorSetDirective_AddsADirectiveInDirectiveOrder()
    {
        var text = await RazorAsync("razor_set_directive", new()
        {
            ["path"] = Card,
            ["directive"] = "using",
            ["value"] = "System.Text",
            ["dryRun"] = true,
        });

        Assert.Contains("+@using System.Text", text, StringComparison.Ordinal);
        Assert.Contains("changedLines=1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorSetDirective_RefusesToRemoveADirectiveTheFileDoesNotDeclare()
    {
        var text = await RazorAsync("razor_set_directive", new()
        {
            ["path"] = Card,
            ["directive"] = "layout",
            ["remove"] = true,
        });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("razor_outline", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindUsages_ReportsAComponentUsedInMarkupAtItsRazorLine()
    {
        var text = (await RazorAsync("find_usages", new() { ["symbolId"] = "T:Fixture.Blazor.Components.Card" }))
            .Replace('\\', '/');

        Assert.Contains(Home + ":6", text, StringComparison.Ordinal);
        Assert.Contains("razor markup", text, StringComparison.Ordinal);
        Assert.DoesNotContain("_razor.g.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchSymbols_FindsAComponentDeclaredInARazorFile()
    {
        var text = (await RazorAsync("search_symbols", new() { ["query"] = "Badge" })).Replace('\\', '/');

        Assert.Contains("src/Fixture.Blazor/Components/Badge.razor", text, StringComparison.Ordinal);
        Assert.Contains("component Badge", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListEndpoints_IncludesEveryPageRoute()
    {
        var text = (await RazorAsync("list_endpoints", [])).Replace('\\', '/');

        Assert.Contains("@page  /order/{Id:int}", text, StringComparison.Ordinal);
        Assert.Contains("src/Fixture.Blazor/Components/Home.razor:1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceStatus_ReportsTheRazorGeneratorHealth()
    {
        var quiet = await RazorAsync("workspace_status", []);
        var text = await RazorAsync("workspace_status", new() { ["verbose"] = true });

        Assert.DoesNotContain("generator=", quiet, StringComparison.Ordinal);
        Assert.Contains("razor=", text, StringComparison.Ordinal);
        Assert.Contains("generator=ok", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorOutline_RefusesANonRazorFile()
    {
        var text = await RazorAsync("razor_outline", new() { ["path"] = "src/Fixture.Blazor/Program.cs" });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains(".razor or .cshtml", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbolBody_OnAMemberDeclaredInCode_EditsTheRazorFile()
    {
        var text = await RazorAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Blazor.Components.Card.Reset",
            ["body"] = "{ expanded = true; }",
            ["dryRun"] = true,
        });

        Assert.Contains("Card.razor", text, StringComparison.Ordinal);
        Assert.Contains("expanded = true;", text, StringComparison.Ordinal);
        Assert.Contains("-        expanded = false;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("_razor.g.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbolBody_OnAnExpressionBodiedRazorMember_RefusesWithARemedy()
    {
        var text = await RazorAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Blazor.Components.Card.Toggle",
            ["body"] = "{ expanded = true; }",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("replace_symbol", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_OnAComponent_InsertsIntoItsCodeBlock()
    {
        var text = await RazorAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Blazor.Components.Card",
            ["declaration"] = "private int clicks;",
            ["dryRun"] = true,
        });

        Assert.Contains("+    private int clicks;", text, StringComparison.Ordinal);
        Assert.Contains("Card.razor", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameSymbol_OnAComponent_RenamesTheFileAndItsMarkupUsages()
    {
        var text = (await RazorAsync("rename_symbol", new()
        {
            ["symbolId"] = "T:Fixture.Blazor.Components.Card",
            ["newName"] = "Panel",
            ["dryRun"] = true,
        })).Replace('\\', '/');

        Assert.Contains("moved src/Fixture.Blazor/Components/Card.razor -> src/Fixture.Blazor/Components/Panel.razor", text, StringComparison.Ordinal);
        Assert.Contains("moved src/Fixture.Blazor/Components/Card.razor.cs -> src/Fixture.Blazor/Components/Panel.razor.cs", text, StringComparison.Ordinal);
        Assert.Contains("+<Panel Title=\"Open orders\"", text, StringComparison.Ordinal);
        Assert.Contains("+public sealed partial class Panel", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorOutline_CostsAFractionOfReadingTheWidestComponent()
    {
        var outline = await RazorAsync("razor_outline", new() { ["path"] = "src/Fixture.Blazor/Components/Dashboard.razor" });
        var whole = await File.ReadAllTextAsync(
            Path.Combine(FixtureRoot, "src", "Fixture.Blazor", "Components", "Dashboard.razor"),
            TestContext.Current.CancellationToken);

        Assert.True(Tokens(outline) * 2 < Tokens(whole), Report("razor_outline", outline, whole));
    }

    [Fact]
    public async Task RazorComponent_StaysUnderItsBudget()
    {
        var text = await RazorAsync("razor_component", new() { ["name"] = "Badge" });

        Assert.True(Tokens(text) <= 200, Report("razor_component", text, text));
    }

    [Fact]
    public async Task RazorValidate_OnTheWholeSolution_StaysUnderItsBudget()
    {
        var text = await RazorAsync("razor_validate", new() { ["scope"] = "solution" });

        Assert.True(Tokens(text) <= 800, Report("razor_validate", text, text));
    }

    private static int Tokens(string text) => (text.Length + 3) / 4;

    private static string Report(string tool, string response, string baseline) => string.Create(
        CultureInfo.InvariantCulture,
        $"{tool}: {Tokens(response)} tokens vs {Tokens(baseline)} for the raw file\n{response}");

    [Fact]
    public async Task RazorSetAttribute_AppliedForReal_WritesTheFileAndCanBeReverted()
    {
        var path = Path.Combine(FixtureRoot, "src", "Fixture.Blazor", "Components", "Dashboard.razor");
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        try
        {
            var applied = await RazorAsync("razor_set_attribute", new()
            {
                ["path"] = "src/Fixture.Blazor/Components/Dashboard.razor",
                ["target"] = "div/section/div/Badge",
                ["attribute"] = "Count",
                ["value"] = "7",
            });

            Assert.Contains("changedLines=", applied, StringComparison.Ordinal);
            Assert.DoesNotContain("ERROR", applied, StringComparison.Ordinal);

            var written = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

            Assert.Contains("Count=\"7\"", written, StringComparison.Ordinal);
            Assert.Equal(before.Length - "@rows.Count".Length + "7".Length, written.Length);
        }
        finally
        {
            await File.WriteAllTextAsync(path, before, TestContext.Current.CancellationToken);
        }
    }

    private Task<string> RazorAsync(string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);
}
