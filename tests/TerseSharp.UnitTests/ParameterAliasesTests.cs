using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ParameterAliasesTests
{
    [Fact]
    public void Apply_RenamesAnObservedSpellingToItsCanonicalParameter()
    {
        var arguments = Arguments("""{"typeName": "OrderService", "all": true}""");

        ParameterAliases.Apply("get_type_outline", arguments);

        Assert.False(arguments.ContainsKey("typeName"));
        Assert.Equal("OrderService", arguments["symbol"].GetString());
        Assert.True(arguments["all"].GetBoolean());
    }

    [Fact]
    public void Apply_KeepsTheAlias_WhenItsCanonicalParameterIsAlsoSupplied()
    {
        var arguments = Arguments("""{"typeName": "Order", "symbol": "OrderService"}""");

        ParameterAliases.Apply("get_type_outline", arguments);

        Assert.Equal("Order", arguments["typeName"].GetString());
        Assert.Equal("OrderService", arguments["symbol"].GetString());
    }

    [Theory]
    [InlineData("count", true)]
    [InlineData("files_with_matches", true)]
    [InlineData("content", false)]
    public void Apply_MapsAGrepOutputModeToCountOnly(string mode, bool countOnly)
    {
        var arguments = Arguments("{\"output_mode\": \"" + mode + "\"}");

        ParameterAliases.Apply("search_text", arguments);

        Assert.False(arguments.ContainsKey("output_mode"));
        Assert.Equal(countOnly, arguments["countOnly"].GetBoolean());
    }

    [Fact]
    public void Apply_LeavesAnOutputModeNothingMaps_ForTheRefusalToName()
    {
        var arguments = Arguments("""{"output_mode": "lines"}""");

        ParameterAliases.Apply("search_text", arguments);

        Assert.Equal("lines", arguments["output_mode"].GetString());
        Assert.False(arguments.ContainsKey("countOnly"));
    }

    [Fact]
    public void Apply_BindsOnlyOneOfTwoAliasesForTheSameParameter_SoTheOtherIsStillRefused()
    {
        var arguments = Arguments("""{"-A": 2, "-C": 3}""");

        ParameterAliases.Apply("search_text", arguments);

        Assert.Equal(3, arguments["context"].GetInt32());
        Assert.Equal(2, arguments["-A"].GetInt32());
        Assert.False(arguments.ContainsKey("-C"));
    }

    [Fact]
    public void Apply_OverAProbeJsonObject_BindsExactlyAsOverTheServerArguments()
    {
        var probe = (JsonObject)JsonNode.Parse("""{"code": "public int X() => 1;", "output_mode": "count"}""")!;

        ParameterAliases.Apply("add_member", probe);

        Assert.Equal("public int X() => 1;", probe["declaration"]!.GetValue<string>());
        Assert.False(probe.ContainsKey("code"));
        Assert.True(probe.ContainsKey("output_mode"));
    }

    [Fact]
    public void Apply_ForAToolWithNoAliases_ChangesNothing()
    {
        var arguments = Arguments("""{"code": "x"}""");

        ParameterAliases.Apply("get_symbol_source", arguments);

        Assert.Equal("code", Assert.Single(arguments.Keys));
    }

    [Fact]
    public void EveryAlias_NamesAParameterItsToolDeclares_AndIsNotItselfDeclared()
    {
        var tools = typeof(ParameterAliases).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(method => (Method: method, method.GetCustomAttribute<McpServerToolAttribute>()?.Name))
            .Where(pair => pair.Name is not null)
            .ToDictionary(pair => pair.Name!, pair => pair.Method.GetParameters().Select(parameter => parameter.Name!).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

        Assert.NotEmpty(ParameterAliases.All);

        foreach (var alias in ParameterAliases.All)
        {
            Assert.True(tools.TryGetValue(alias.Tool, out var declared), alias.Tool + " is not an advertised tool");
            Assert.Contains(alias.Canonical, declared);
            Assert.DoesNotContain(alias.Alias, declared);
        }
    }

    private static Dictionary<string, JsonElement> Arguments(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
}
