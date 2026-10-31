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

    [Fact]
    public void Apply_BindsReadsOffsetAndLimit_ToReadTextsStartLineAndMaxLines()
    {
        var arguments = Arguments("""{"path": "README.md", "offset": 40, "limit": 20}""");

        ParameterAliases.Apply("read_text", arguments);

        Assert.Equal(40, arguments["startLine"].GetInt32());
        Assert.Equal(20, arguments["maxLines"].GetInt32());
        Assert.False(arguments.ContainsKey("offset"));
        Assert.False(arguments.ContainsKey("limit"));
        Assert.Equal("README.md", arguments["path"].GetString());
    }

    [Fact]
    public void Apply_JoinsReadTextsRootOntoARelativePath()
    {
        var root = Path.GetFullPath(Path.GetTempPath());
        var arguments = Arguments(JsonSerializer.Serialize(new Dictionary<string, string> { ["root"] = root, ["path"] = "notes/a.md" }));

        ParameterAliases.Apply("read_text", arguments);

        Assert.False(arguments.ContainsKey("root"));
        Assert.Equal(Path.Join(root, "notes/a.md"), arguments["path"].GetString());
    }

    [Fact]
    public void Apply_KeepsReadTextsRoot_WhenThePathIsAlreadyAbsoluteOrAbsent()
    {
        var root = Path.GetFullPath(Path.GetTempPath());
        var absolute = Path.Join(root, "a.md");
        var beside = Arguments(JsonSerializer.Serialize(new Dictionary<string, string> { ["root"] = root, ["path"] = absolute }));
        var alone = Arguments(JsonSerializer.Serialize(new Dictionary<string, string> { ["root"] = root }));

        ParameterAliases.Apply("read_text", beside);
        ParameterAliases.Apply("read_text", alone);

        Assert.True(beside.ContainsKey("root"));
        Assert.Equal(absolute, beside["path"].GetString());
        Assert.True(alone.ContainsKey("root"));
        Assert.False(alone.ContainsKey("path"));
    }

    [Fact]
    public void Apply_BindsChangedFilesTrackedToTheInverseOfUntracked()
    {
        var tracked = Arguments("""{"tracked": true}""");
        var everything = Arguments("""{"tracked": false}""");
        var contradicted = Arguments("""{"tracked": true, "untracked": true}""");

        ParameterAliases.Apply("changed_files", tracked);
        ParameterAliases.Apply("changed_files", everything);
        ParameterAliases.Apply("changed_files", contradicted);

        Assert.False(tracked["untracked"].GetBoolean());
        Assert.False(tracked.ContainsKey("tracked"));
        Assert.True(everything["untracked"].GetBoolean());
        Assert.True(contradicted.ContainsKey("tracked"));
    }

    [Fact]
    public void Apply_LeavesChangedFilesPathsForTheToolThatDeclaresIt()
    {
        var one = Arguments("""{"paths": ["src"]}""");
        var two = Arguments("""{"paths": ["src", "tests"]}""");

        ParameterAliases.Apply("changed_files", one);
        ParameterAliases.Apply("changed_files", two);

        Assert.Equal(1, one["paths"].GetArrayLength());
        Assert.False(one.ContainsKey("path"));
        Assert.Equal(2, two["paths"].GetArrayLength());
        Assert.False(two.ContainsKey("path"));
    }

    [Fact]
    public void Apply_OverAProbeJsonObject_JoinsRootAndInvertsTrackedExactlyAsOverTheServerArguments()
    {
        var root = Path.GetFullPath(Path.GetTempPath());
        var read = new JsonObject { ["root"] = root, ["path"] = "a.md" };
        var changed = (JsonObject)JsonNode.Parse("""{"tracked": true, "paths": ["src"]}""")!;

        ParameterAliases.Apply("read_text", read);
        ParameterAliases.Apply("changed_files", changed);

        Assert.Equal(Path.Join(root, "a.md"), read["path"]!.GetValue<string>());
        Assert.False(read.ContainsKey("root"));
        Assert.False(changed["untracked"]!.GetValue<bool>());
        Assert.Equal("src", changed["paths"]![0]!.GetValue<string>());
        Assert.False(changed.ContainsKey("path"));
    }

    [Theory]
    [InlineData("get_type_outline", "symbols", "symbolIds")]
    [InlineData("get_symbol", "symbols", "symbolIds")]
    [InlineData("get_symbol_source", "symbols", "symbolIds")]
    [InlineData("add_member", "symbolIds", "typeSymbolIds")]
    [InlineData("find_files", "paths", "globs")]
    [InlineData("search_text", "globs", "paths")]
    [InlineData("search_regex", "globs", "paths")]
    public void Apply_BindsASiblingToolsBatchSpelling_ToThisToolsOwnBatchParameter(string tool, string alias, string canonical)
    {
        var arguments = Arguments(string.Concat("{\"", alias, "\": [\"a\", \"b\"]}"));
        string[] expected = ["a", "b"];

        ParameterAliases.Apply(tool, arguments);

        Assert.False(arguments.ContainsKey(alias));
        Assert.Equal(expected, arguments[canonical].EnumerateArray().Select(entry => entry.GetString()!).ToArray());
    }

    [Theory]
    [InlineData("diff_text", """{"context": 3}""", "unified", "3")]
    [InlineData("replace_symbol", """{"newSource": "void M() { }"}""", "declaration", "\"void M() { }\"")]
    public void Apply_BindsAnObservedScalarSpelling_ToTheParameterOfTheSameMeaning(string tool, string json, string canonical, string expected)
    {
        var arguments = Arguments(json);

        ParameterAliases.Apply(tool, arguments);

        Assert.Single(arguments);
        Assert.Equal(expected, arguments[canonical].GetRawText());
    }
}
