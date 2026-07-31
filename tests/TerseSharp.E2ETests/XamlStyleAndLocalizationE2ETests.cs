namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class XamlStyleAndLocalizationE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task XamlStyles_ListsKeyedAndImplicitStylesForAType()
    {
        var text = await server.CallAsync("xaml_styles", new() { ["typeName"] = "Button" });

        Assert.Contains("implicit  key=-  targets=Button", text, StringComparison.Ordinal);
        Assert.Contains("key=BaseButton", text, StringComparison.Ordinal);
        Assert.Contains("3 styles", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlStyles_ResolvesTheBasedOnChain()
    {
        var text = await server.CallAsync("xaml_styles", new() { ["typeName"] = "Button" });

        Assert.Contains("basedOn=BaseButton", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlStyles_ForATypeNothingTargets_SaysSo()
    {
        var text = await server.CallAsync("xaml_styles", new() { ["typeName"] = "NoSuchControl" });

        Assert.Contains("0 styles", text, StringComparison.Ordinal);
        Assert.Contains("no Style targets", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlStyles_AcceptsAQualifiedTypeName()
    {
        var text = await server.CallAsync("xaml_styles", new() { ["typeName"] = "sys:Button" });

        Assert.Contains("targets=Button", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlLocalization_JoinsAUidToItsResourceEntry()
    {
        var text = await server.CallAsync("xaml_localization", []);

        Assert.Contains("uid=BoundView_Symbol", text, StringComparison.Ordinal);
        Assert.Contains("Strings.resx#BoundView_Symbol.Text", Slashes(text), StringComparison.Ordinal);
        Assert.Contains("EXACT", text, StringComparison.Ordinal);
    }

    private static string Slashes(string text) => text.Replace(Path.DirectorySeparatorChar, '/');

    [Fact]
    public async Task XamlLocalization_ReportsAUidWithNoEntryRatherThanOmittingIt()
    {
        var text = await server.CallAsync("xaml_localization", []);

        Assert.Contains("resourceFiles=1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }
}
