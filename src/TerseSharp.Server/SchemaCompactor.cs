using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TerseSharp.Server;

public static class SchemaCompactor
{
    private const string Default = "default";

    private const string Type = "type";

    private const string Null = "null";

    private static readonly ConcurrentDictionary<string, JsonElement> Cache = new(StringComparer.Ordinal);

    public static McpRequestFilter<ListToolsRequestParams, ListToolsResult> Filter() =>
        next => async (request, cancellationToken) =>
        {
            var listed = await next(request, cancellationToken).ConfigureAwait(false);

            foreach (var tool in listed.Tools)
                tool.InputSchema = Cache.GetOrAdd(tool.Name, static (_, schema) => Compact(schema), tool.InputSchema);

            return listed;
        };

    public static JsonElement Compact(JsonElement schema)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteRoot(schema, writer);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);

        return document.RootElement.Clone();
    }

    private const string Properties = "properties";

    private static void WriteRoot(JsonElement schema, Utf8JsonWriter writer)
    {
        if (schema.ValueKind is not JsonValueKind.Object)
        {
            Write(schema, writer);

            return;
        }

        writer.WriteStartObject();

        foreach (var property in schema.EnumerateObject())
        {
            if (property.NameEquals(Properties) && property.Value.ValueKind is JsonValueKind.Object)
            {
                writer.WritePropertyName(property.Name);
                WriteParameters(property.Value, writer);
            }
            else
            {
                WriteProperty(property, writer);
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteParameters(JsonElement parameters, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();

        foreach (var parameter in parameters.EnumerateObject())
        {
            writer.WritePropertyName(parameter.Name);
            WriteParameter(parameter.Value, writer);
        }

        writer.WriteEndObject();
    }

    private static void WriteParameter(JsonElement schema, Utf8JsonWriter writer)
    {
        if (schema.ValueKind is not JsonValueKind.Object)
        {
            Write(schema, writer);

            return;
        }

        writer.WriteStartObject();

        foreach (var property in schema.EnumerateObject())
        {
            if (!property.NameEquals(Default))
                WriteProperty(property, writer);
        }

        writer.WriteEndObject();
    }

    private static void Write(JsonElement node, Utf8JsonWriter writer)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(node, writer);
                break;
            case JsonValueKind.Array:
                WriteArray(node, writer);
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    private static void WriteObject(JsonElement node, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();

        foreach (var property in node.EnumerateObject())
            WriteProperty(property, writer);

        writer.WriteEndObject();
    }

    private static void WriteArray(JsonElement node, Utf8JsonWriter writer)
    {
        writer.WriteStartArray();

        foreach (var item in node.EnumerateArray())
            Write(item, writer);

        writer.WriteEndArray();
    }

    private static void WriteProperty(JsonProperty property, Utf8JsonWriter writer)
    {
        if (Union(property) is { } single)
        {
            writer.WriteString(property.Name, single);

            return;
        }

        writer.WritePropertyName(property.Name);
        Write(property.Value, writer);
    }

    private static string? Union(JsonProperty property) =>
        property.NameEquals(Type) ? SingleType(property.Value) : null;

    private static string? SingleType(JsonElement type)
    {
        if (type.ValueKind is not JsonValueKind.Array)
            return null;

        string? kept = null;

        foreach (var arm in type.EnumerateArray())
        {
            var name = arm.ValueKind is JsonValueKind.String ? arm.GetString() : null;

            if (string.Equals(name, Null, StringComparison.Ordinal))
                continue;

            if (name is null || kept is not null)
                return null;

            kept = name;
        }

        return kept;
    }
}
