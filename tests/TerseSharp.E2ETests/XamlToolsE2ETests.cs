namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class XamlToolsE2ETests(TerseServerFixture server)
{
    private const string View = "src/Fixture.Trading/Views/OrderView.xaml";
    private const string BoundView = "src/Fixture.Trading/Views/BoundView.xaml";
    private const string AvaloniaView = "src/Fixture.Trading/Views/Avalonia/MainWindow.axaml";
    private const string MauiView = "src/Fixture.Trading/Views/Maui/MainPage.xaml";
    private const string WinUiView = "src/Fixture.Trading/Views/WinUi/MainPage.xaml";

    [Fact]
    public async Task XamlOutline_ShowsTheElementTreeWithoutAttributes()
    {
        var text = await server.CallAsync("xaml_outline", new() { ["path"] = View });

        Assert.Contains("dialect=wpf", text, StringComparison.Ordinal);
        Assert.Contains("Grid", text, StringComparison.Ordinal);
        Assert.Contains("#SymbolText", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding Symbol}", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlNames_ListsEveryName()
    {
        var text = await server.CallAsync("xaml_names", new() { ["path"] = View });

        Assert.Contains("SymbolText", text, StringComparison.Ordinal);
        Assert.Contains("VolumeText", text, StringComparison.Ordinal);
        Assert.Contains("4 names", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlResources_ListsKeyedResources()
    {
        var text = await server.CallAsync("xaml_resources", new() { ["path"] = View });

        Assert.Contains("AccentBrush", text, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlBindings_FindsEveryBindingExpression()
    {
        var text = await server.CallAsync("xaml_bindings", new() { ["path"] = View });

        Assert.Contains("{Binding Symbol}", text, StringComparison.Ordinal);
        Assert.Contains("{Binding Volume, Mode=OneWay}", text, StringComparison.Ordinal);
        Assert.Contains("2 bindings", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlValidate_ReportsDuplicateKeysNamesAndMissingResources()
    {
        var text = await server.CallAsync("xaml_validate", new() { ["path"] = View });

        Assert.Contains("XAML001", text, StringComparison.Ordinal);
        Assert.Contains("XAML002", text, StringComparison.Ordinal);
        Assert.Contains("XAML003", text, StringComparison.Ordinal);
        Assert.Contains("MissingBrush", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlFind_ByElementType_FindsTheButtons()
    {
        var text = await server.CallAsync("xaml_find", new() { ["query"] = "Button" });

        Assert.Contains("OrderView.xaml", text, StringComparison.Ordinal);
        Assert.Contains("2 matches", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlFind_ByName_FindsThatElement()
    {
        var text = await server.CallAsync("xaml_find", new() { ["query"] = "VolumeText", ["kind"] = "name" });

        Assert.Contains("TextBlock", text, StringComparison.Ordinal);
        Assert.Contains("1 matches", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlOutline_OnACsFile_IsRefused()
    {
        var text = await server.CallAsync("xaml_outline", new() { ["path"] = "src/Fixture.Trading/Order.cs" });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlOutline_OnAnAvaloniaFile_ReportsTheAvaloniaDialect()
    {
        var text = await server.CallAsync("xaml_outline", new() { ["path"] = AvaloniaView });

        Assert.Contains("dialect=avalonia", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlOutline_OnAMauiFile_ReportsTheMauiDialect()
    {
        var text = await server.CallAsync("xaml_outline", new() { ["path"] = MauiView });

        Assert.Contains("dialect=maui", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlOutline_OnAWinUiFile_ReportsTheWinUiDialect()
    {
        var text = await server.CallAsync("xaml_outline", new() { ["path"] = WinUiView });

        Assert.Contains("dialect=winui", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlOutline_WithNamedFilter_ListsOnlyNamedElements()
    {
        var text = await server.CallAsync("xaml_outline", new() { ["path"] = View, ["filter"] = "named" });

        Assert.Contains("#SymbolText", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SolidColorBrush", text, StringComparison.Ordinal);
        Assert.Contains("total=9", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlResolve_ReportsEveryDeclarationWithItsScope()
    {
        var text = await server.CallAsync("xaml_resolve", new() { ["key"] = "AccentBrush" });

        Assert.Contains("Views/Themes/Dark.xaml", text.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Contains("Views/OrderView.xaml", text.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Contains("scope=theme", text, StringComparison.Ordinal);
        Assert.Contains("scope=local", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlResolve_ReportsTheAppScopeOfAnApplicationResource()
    {
        var text = await server.CallAsync("xaml_resolve", new() { ["key"] = "ShellBrush" });

        Assert.Contains("scope=app", text, StringComparison.Ordinal);
        Assert.Contains("1 declarations", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlResolve_OnAnUndeclaredKey_SaysSoInsteadOfAnsweringEmpty()
    {
        var text = await server.CallAsync("xaml_resolve", new() { ["key"] = "MissingBrush" });

        Assert.Contains("0 declarations", text, StringComparison.Ordinal);
        Assert.Contains("declared in no XAML file", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlValidate_DoesNotFlagAResourceDeclaredInAnotherFile()
    {
        var text = await server.CallAsync("xaml_validate", new() { ["path"] = BoundView });

        Assert.DoesNotContain("SurfaceBrush", text, StringComparison.Ordinal);
        Assert.Contains("0 issues", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlValidate_OverTheSolution_AggregatesEveryFile()
    {
        var text = await server.CallAsync("xaml_validate", new() { ["scope"] = "solution" });

        Assert.Contains("XAML003", text, StringComparison.Ordinal);
        Assert.Contains("MissingBrush", text, StringComparison.Ordinal);
        Assert.Contains("scanned=", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlBindings_WithValidate_ResolvesThePathAgainstTheDesignInstanceType()
    {
        var text = await server.CallAsync("xaml_bindings", new() { ["path"] = BoundView, ["validate"] = true });

        Assert.Contains("EXACT", text, StringComparison.Ordinal);
        Assert.Contains("OK Symbol on Fixture.Trading.Views.OrderViewModel", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlBindings_WithValidate_NamesTheMissingMemberAndTheNearestOne()
    {
        var text = await server.CallAsync("xaml_bindings", new() { ["path"] = BoundView, ["validate"] = true });

        Assert.Contains("ERROR no member 'Symbl'", text, StringComparison.Ordinal);
        Assert.Contains("nearest 'Symbol'", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlBindings_WithValidate_WalksANestedPath()
    {
        var text = await server.CallAsync("xaml_bindings", new() { ["path"] = BoundView, ["validate"] = true });

        Assert.Contains("OK Selected.Symbol on Fixture.Trading.Views.OrderViewModel", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlBindings_WithValidate_OnAnAvaloniaDataType_ChecksTheCompiledBinding()
    {
        var text = await server.CallAsync("xaml_bindings", new() { ["path"] = AvaloniaView, ["validate"] = true });

        Assert.Contains("OK Symbol on Fixture.Trading.Views.OrderViewModel", text, StringComparison.Ordinal);
        Assert.Contains("ERROR no member 'Missing'", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlBindings_WithValidate_WithNoDataContext_SaysUnresolvedRatherThanClaimingAnError()
    {
        var text = await server.CallAsync("xaml_bindings", new() { ["path"] = View, ["validate"] = true });

        Assert.Contains("UNRESOLVED_CONTEXT", text, StringComparison.Ordinal);
        Assert.Contains("HEURISTIC", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlNames_ReportsTheUidOfAnElement()
    {
        var text = await server.CallAsync("xaml_names", new() { ["path"] = BoundView });

        Assert.Contains("uid=BoundView_Symbol", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlFind_ByUid_FindsThatElement()
    {
        var text = await server.CallAsync("xaml_find", new() { ["query"] = "BoundView_Symbol", ["kind"] = "uid" });

        Assert.Contains("TextBlock", text, StringComparison.Ordinal);
        Assert.Contains("1 matches", text, StringComparison.Ordinal);
    }
}
