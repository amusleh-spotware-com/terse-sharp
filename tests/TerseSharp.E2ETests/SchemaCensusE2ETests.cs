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

    [Fact]
    public async Task EveryAdvertisedTool_IsClassifiedAndCarriesTheAnnotationItsClassDeclares()
    {
        var surface = await Surface();
        var advertised = surface.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var classified = ToolCensus.ReadOnlyTools
            .Concat(ToolCensus.DestructiveTools)
            .Concat(ToolCensus.MutatingTools)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(advertised, classified);

        var annotated = surface.Where(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint is true).Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var destructive = surface.Where(tool => tool.ProtocolTool.Annotations?.DestructiveHint is true).Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(ToolCensus.ReadOnlyTools.OrderBy(name => name, StringComparer.Ordinal).ToArray(), annotated);
        Assert.Equal(ToolCensus.DestructiveTools.OrderBy(name => name, StringComparer.Ordinal).ToArray(), destructive);
    }

    [Fact]
    public async Task NoMutatingTool_ClaimsToBeReadOnly()
    {
        var surface = await Surface();
        var claimed = surface
            .Where(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint is true)
            .Select(tool => tool.Name)
            .Intersect(ToolCensus.MutatingTools.Concat(ToolCensus.DestructiveTools), StringComparer.Ordinal)
            .ToArray();

        Assert.True(claimed.Length is 0, "mutating tools claiming readOnlyHint: " + string.Join(", ", claimed));
        Assert.True(ToolCensus.ReadOnlyTools.Length >= 40, string.Create(CultureInfo.InvariantCulture, $"only {ToolCensus.ReadOnlyTools.Length} tools are classified read-only"));
    }

    [Fact]
    public async Task EveryToolWithAPluralParameter_NamesItImperativelyInItsDescription()
    {
        var offenders = new List<string>();
        var examined = 0;

        foreach (var tool in await Surface())
        {
            foreach (var plural in Plurals(tool))
            {
                examined++;

                if (!Names(tool.Description, plural))
                    offenders.Add(tool.Name + "." + plural);
            }
        }

        Assert.True(examined >= 5, string.Create(CultureInfo.InvariantCulture, $"the census found only {examined} plural parameters"));
        Assert.True(offenders.Count is 0, "plural parameters not named imperatively: " + string.Join(", ", offenders));
    }

    private static bool Names(string? description, string plural) =>
        description is { Length: > 0 } text
        && text.Contains(plural, StringComparison.Ordinal)
        && text.Contains("Replaces one call per", StringComparison.Ordinal);

    private static string[] Plurals(McpClientTool tool)
    {
        if (!tool.JsonSchema.TryGetProperty("properties", out var properties))
            return [];

        var names = properties.EnumerateObject().Select(property => property.Name).ToArray();

        return [.. names.Where(name => IsArray(properties, name) && Singular(name) is { } single && names.Contains(single, StringComparer.Ordinal))];
    }

    private static string? Singular(string plural) => plural switch
    {
        { Length: > 3 } when plural.EndsWith("ies", StringComparison.Ordinal) => plural[..^3] + "y",
        { Length: > 1 } when plural.EndsWith('s') => plural[..^1],
        _ => null,
    };


    private static bool IsArray(JsonElement properties, string name) =>
    properties.TryGetProperty(name, out var property)
    && property.TryGetProperty("type", out var type)
    && (Declares(type) || type.ValueKind is JsonValueKind.Array && type.EnumerateArray().Any(Declares));

    private static bool Declares(JsonElement type) =>
        type.ValueKind is JsonValueKind.String && string.Equals(type.GetString(), "array", StringComparison.Ordinal);

    [Fact]
    public async Task NoAdvertisedSchema_CarriesANullDefaultOrANullTypeArm()
    {
        var surface = await Surface();
        var noisy = surface.Where(tool => Noisy(tool.JsonSchema)).Select(tool => tool.Name).ToArray();
        var payload = surface.Sum(tool => tool.JsonSchema.GetRawText().Length);

        Assert.True(noisy.Length is 0, "schemas still carrying serializer noise: " + string.Join(", ", noisy));
        Assert.True(payload > 20000, string.Create(CultureInfo.InvariantCulture, $"only {payload} characters of schema were examined"));
    }

    private static bool Noisy(JsonElement node) => node.ValueKind switch
    {
        JsonValueKind.Object => node.EnumerateObject().Any(NoisyProperty),
        JsonValueKind.Array => node.EnumerateArray().Any(Noisy),
        _ => false,
    };


    private static bool NoisyProperty(JsonProperty property) =>
        property.Value.ValueKind is JsonValueKind.Null && property.NameEquals("default")
        || property.NameEquals("type") && HasNullArm(property.Value)
        || Noisy(property.Value);


    private static bool HasNullArm(JsonElement type) =>
        type.ValueKind is JsonValueKind.Array
        && type.EnumerateArray().Any(arm => string.Equals(arm.GetString(), "null", StringComparison.Ordinal));

    [Fact]
    public async Task EveryToolWithAPluralParameter_IsKnownToTheRepeatSteerAndNoOtherIs()
    {
        var declared = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var tool in await Surface())
        {
            if (Plurals(tool) is [var plural, ..])
                declared[tool.Name] = plural;
        }

        var known = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in TerseSharp.Server.RepeatSteer.Plural)
        {
            if (Has(await Surface(), entry.Key, entry.Value))
                known[entry.Key] = entry.Value;
        }

        Assert.True(declared.Count >= 5, string.Create(CultureInfo.InvariantCulture, $"the census found only {declared.Count} plural parameters"));

        var missing = declared.Keys.Where(name => !TerseSharp.Server.RepeatSteer.Plural.ContainsKey(name)).ToArray();
        var unknown = TerseSharp.Server.RepeatSteer.Plural.Keys.Where(name => !known.ContainsKey(name)).ToArray();

        Assert.True(missing.Length is 0, "tools with a plural parameter the repeat steer does not know: " + string.Join(", ", missing));
        Assert.True(unknown.Length is 0, "repeat-steer entries naming a parameter no advertised tool declares: " + string.Join(", ", unknown));
    }

    private static bool Has(IList<McpClientTool> surface, string tool, string parameter) =>
        surface.FirstOrDefault(entry => string.Equals(entry.Name, tool, StringComparison.Ordinal)) is { } found
        && Has(found, parameter);

    [Fact]
    public async Task EveryToolTheCodePolicyCanBlock_TakesAllowPolicy()
    {
        var surface = await Surface();
        var mutating = surface.Where(tool => Has(tool, "dryRun")).ToArray();

        var missing = mutating
            .Where(tool => !Has(tool, "allowPolicy") && !Exempt(tool.Name))
            .Select(tool => tool.Name)
            .ToArray();

        Assert.NotEmpty(mutating);
        Assert.True(missing.Length is 0, "tools the code policy gates with no allowPolicy: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task EveryPolicyExemption_CarriesAReasonNamesAnAdvertisedToolAndTheSetOnlyShrinks()
    {
        var surface = await Surface();

        Assert.True(
            ToolCensus.PolicyExempt.Length <= ToolCensus.MaxPolicyExemptions,
            string.Create(CultureInfo.InvariantCulture, $"{ToolCensus.PolicyExempt.Length} policy exemptions against a ratchet of {ToolCensus.MaxPolicyExemptions}"));

        Assert.All(ToolCensus.PolicyExempt, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Reason), entry.Tool));
        Assert.All(
            ToolCensus.PolicyExempt,
            entry => Assert.Contains(surface, tool => string.Equals(tool.Name, entry.Tool, StringComparison.Ordinal)));
    }

    private static bool Exempt(string tool) =>
        Array.Exists(ToolCensus.PolicyExempt, entry => string.Equals(entry.Tool, tool, StringComparison.Ordinal));
}
