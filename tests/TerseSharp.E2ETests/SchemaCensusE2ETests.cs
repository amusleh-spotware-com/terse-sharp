using System.Text.Json;
using ModelContextProtocol.Client;

namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class SchemaCensusE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task EveryMutatingTool_TakesVerbose()
    {
        var missing = (await Surface())
            .Where(tool => Has(tool, "dryRun") && !Has(tool, "verbose"))
            .Select(tool => tool.Name)
            .ToArray();

        Assert.True(missing.Length is 0, "mutating tools with no verbose: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task EverySymbolIdTool_TakesTheSymbolAlias()
    {
        var missing = (await Surface())
            .Where(tool => SymbolIdNames(tool).Length > 0 && !Has(tool, "symbol"))
            .Select(tool => tool.Name)
            .ToArray();

        Assert.True(missing.Length is 0, "symbolId tools with no symbol alias: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task NoAdvertisedTool_DeclaresASymbolIdAsRequired()
    {
        var required = (await Surface())
            .Where(tool => Required(tool).Intersect(SymbolIdNames(tool), StringComparer.Ordinal).Any())
            .Select(tool => tool.Name)
            .ToArray();

        Assert.True(required.Length is 0, "tools declaring a symbol id required: " + string.Join(", ", required));
    }

    [Fact]
    public async Task TheCensusItselfCoversTheWholeSurface()
    {
        var surface = await Surface();
        var withSymbolIds = surface.Where(tool => SymbolIdNames(tool).Length > 0).ToArray();

        Assert.Equal(ToolCoverageE2ETests.ExercisedCount, surface.Count);
        Assert.True(withSymbolIds.Length >= 15, $"only {withSymbolIds.Length} tools were seen to take a symbol id");
        Assert.Contains(surface, tool => Has(tool, "dryRun"));
    }

    private async Task<IList<McpClientTool>> Surface() =>
        await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

    private static bool Has(McpClientTool tool, string parameter) =>
        tool.JsonSchema.TryGetProperty("properties", out var properties)
        && properties.TryGetProperty(parameter, out _);

    private static string[] Required(McpClientTool tool) =>
        tool.JsonSchema.TryGetProperty("required", out var required) && required.ValueKind is JsonValueKind.Array
            ? [.. required.EnumerateArray().Select(entry => entry.GetString() ?? string.Empty)]
            : [];

    private static string[] SymbolIdNames(McpClientTool tool) =>
            !tool.JsonSchema.TryGetProperty("properties", out var properties)
                ? []
                : [.. properties.EnumerateObject()
                .Select(property => property.Name)
                .Where(name => name.EndsWith("symbolId", StringComparison.Ordinal) || name.EndsWith("SymbolId", StringComparison.Ordinal))];
}
