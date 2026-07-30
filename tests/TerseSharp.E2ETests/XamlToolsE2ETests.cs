namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class XamlToolsE2ETests(TerseServerFixture server)
{
    private const string View = "src/Fixture.Trading/Views/OrderView.xaml";

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
}
