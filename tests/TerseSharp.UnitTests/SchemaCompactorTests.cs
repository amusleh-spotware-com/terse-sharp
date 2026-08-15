using System.Text.Json;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class SchemaCompactorTests
{
    [Fact]
    public void Compact_DropsEveryDefaultAndKeepsTheRestOfTheProperty()
    {
        var compacted = Compact("""
            {"properties":{"a":{"type":"string","default":null},"b":{"type":"boolean","default":false},"c":{"type":"integer","default":600,"description":"kept"}}}
            """);

        var properties = compacted.GetProperty("properties");

        Assert.False(properties.GetProperty("a").TryGetProperty("default", out _));
        Assert.False(properties.GetProperty("b").TryGetProperty("default", out _));
        Assert.False(properties.GetProperty("c").TryGetProperty("default", out _));
        Assert.Equal("integer", properties.GetProperty("c").GetProperty("type").GetString());
        Assert.Equal("kept", properties.GetProperty("c").GetProperty("description").GetString());
    }

    [Fact]
    public void Compact_CollapsesATwoArmUnionWhoseSecondArmIsNull()
    {
        var compacted = Compact("""{"properties":{"a":{"type":["string","null"]},"b":{"type":["array","null"]}}}""");
        var properties = compacted.GetProperty("properties");

        Assert.Equal("string", properties.GetProperty("a").GetProperty("type").GetString());
        Assert.Equal("array", properties.GetProperty("b").GetProperty("type").GetString());
    }

    [Fact]
    public void Compact_KeepsAUnionThatCarriesMoreThanOneRealType()
    {
        var compacted = Compact("""{"properties":{"a":{"type":["string","integer","null"]}}}""");
        var type = compacted.GetProperty("properties").GetProperty("a").GetProperty("type");

        Assert.Equal(JsonValueKind.Array, type.ValueKind);
        Assert.Equal(3, type.GetArrayLength());
    }

    [Fact]
    public void Compact_ReachesTheItemsSchemaOfAnArrayParameter()
    {
        var compacted = Compact("""{"properties":{"paths":{"type":["array","null"],"items":{"type":["string","null"]},"default":null}}}""");
        var paths = compacted.GetProperty("properties").GetProperty("paths");

        Assert.Equal("array", paths.GetProperty("type").GetString());
        Assert.Equal("string", paths.GetProperty("items").GetProperty("type").GetString());
        Assert.False(paths.TryGetProperty("default", out _));
    }

    [Fact]
    public void Compact_LeavesARequiredArrayOfNamesAlone()
    {
        var compacted = Compact("""{"required":["path","query"],"type":"object"}""");

        Assert.Equal(["path", "query"], compacted.GetProperty("required").EnumerateArray().Select(entry => entry.GetString()));
        Assert.Equal("object", compacted.GetProperty("type").GetString());
    }

    [Fact]
    public void Compact_IsIdempotent()
    {
        var once = Compact("""{"properties":{"a":{"type":["string","null"],"default":null}}}""");
        var twice = SchemaCompactor.Compact(once);

        Assert.Equal(once.GetRawText(), twice.GetRawText());
    }

    [Fact]
    public void Compact_KeepsANullValueThatIsNotADefault()
    {
        var compacted = Compact("""{"properties":{"a":{"type":"string","const":null}}}""");

        Assert.Equal(JsonValueKind.Null, compacted.GetProperty("properties").GetProperty("a").GetProperty("const").ValueKind);
    }

    private static JsonElement Compact(string schema)
    {
        using var document = JsonDocument.Parse(schema);

        return SchemaCompactor.Compact(document.RootElement);
    }
}
