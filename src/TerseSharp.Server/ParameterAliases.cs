using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TerseSharp.Server;

internal enum AliasShape
{
    Renamed,
    Inverted,
    Rooted,
}

internal sealed record ParameterAlias(string Tool, string Alias, string Canonical, FrozenDictionary<string, bool>? Values = null, AliasShape Shape = AliasShape.Renamed);

internal static class ParameterAliases
{
    private static readonly FrozenDictionary<string, bool> OutputModes = new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        ["content"] = false,
        ["count"] = true,
        ["files_with_matches"] = true,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static ImmutableArray<ParameterAlias> All { get; } =
    [
        new("get_type_outline", "typeName", "symbol"),
        new("load_workspace", "name", "path"),
        new("add_member", "code", "declaration"),
        new("add_member", "content", "declaration"),
        new("replace_symbol", "code", "declaration"),
        new("replace_symbol", "content", "declaration"),
        new("list_projects", "contains", "filter"),
        new("read_text", "offset", "startLine"),
        new("read_text", "limit", "maxLines"),
        new("read_text", "root", "path", Shape: AliasShape.Rooted),
        new("changed_files", "tracked", "untracked", Shape: AliasShape.Inverted),
        new("search_text", "-C", "context"),
        new("search_text", "-A", "context"),
        new("search_text", "-B", "context"),
        new("search_text", "output_mode", "countOnly", OutputModes),
        new("search_regex", "-C", "context"),
        new("search_regex", "-A", "context"),
        new("search_regex", "-B", "context"),
        new("search_regex", "output_mode", "countOnly", OutputModes),
    ];

    private static readonly FrozenDictionary<string, ParameterAlias[]> ByTool = All
        .GroupBy(alias => alias.Tool, StringComparer.Ordinal)
        .ToFrozenDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

    public static void Apply(string? tool, IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is not { Count: > 0, IsReadOnly: false })
            return;

        foreach (var alias in For(tool))
        {
            if (!arguments.TryGetValue(alias.Alias, out var value) || Bound(alias, value, arguments.TryGetValue(alias.Canonical, out var current) ? current : null) is not { } converted)
                continue;

            arguments.Remove(alias.Alias);
            arguments[alias.Canonical] = converted;
        }
    }

    public static void Apply(string? tool, JsonObject arguments)
    {
        foreach (var alias in For(tool))
        {
            if (!arguments.TryGetPropertyValue(alias.Alias, out var value) || Bound(alias, value, arguments.ContainsKey(alias.Canonical), arguments[alias.Canonical]) is not { } converted)
                continue;

            arguments.Remove(alias.Alias);
            arguments[alias.Canonical] = converted;
        }
    }

    private static ReadOnlySpan<ParameterAlias> For(string? tool) =>
        tool is not null && ByTool.TryGetValue(tool, out var aliases) ? aliases : [];

    private static JsonElement? Bound(ParameterAlias alias, JsonElement value, JsonElement? current) => (alias.Shape, current) switch
    {
        (AliasShape.Rooted, { } path) => Rooted(Text(value), Text(path)) is { } joined ? Quoted(joined) : null,
        (AliasShape.Rooted, null) => null,
        (_, null) => Converted(alias, value),
        _ => null,
    };

    private static JsonNode? Bound(ParameterAlias alias, JsonNode? value, bool present, JsonNode? current) => (alias.Shape, present) switch
    {
        (AliasShape.Rooted, true) => Rooted(Text(value), Text(current)) is { } joined ? JsonValue.Create(joined) : null,
        (AliasShape.Rooted, false) => null,
        (_, false) => Converted(alias, value),
        _ => null,
    };

    private static string? Rooted(string? root, string? path) =>
        Qualified(root) && path is { Length: > 0 } && !Path.IsPathFullyQualified(path) ? Path.Join(root, path) : null;

    private static bool Qualified(string? root) => root is { Length: > 0 } && Path.IsPathFullyQualified(root);

    private static JsonElement? Converted(ParameterAlias alias, JsonElement value) => (alias.Shape, alias.Values, value.ValueKind) switch
    {
        (AliasShape.Inverted, _, JsonValueKind.True or JsonValueKind.False) => Flag(value.ValueKind is JsonValueKind.False),
        (AliasShape.Inverted, _, _) => null,
        (_, null, _) => value,
        (_, { } values, JsonValueKind.String) when values.TryGetValue(value.GetString()!, out var flag) => Flag(flag),
        _ => null,
    };

    private static JsonNode? Converted(ParameterAlias alias, JsonNode? value) => (alias.Shape, alias.Values, value) switch
    {
        (_, _, null) => null,
        (AliasShape.Inverted, _, JsonValue flag) when flag.TryGetValue<bool>(out var on) => JsonValue.Create(!on),
        (AliasShape.Inverted, _, _) => null,
        (_, null, _) => value,
        (_, { } values, JsonValue text) when text.TryGetValue<string>(out var mode) && values.TryGetValue(mode, out var flag) => JsonValue.Create(flag),
        _ => null,
    };

    private static string? Text(JsonElement value) => value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static JsonElement Flag(bool on) => JsonElement.Parse(on ? "true" : "false");

    private static JsonElement Quoted(string text) => JsonElement.Parse(string.Concat("\"", JsonEncodedText.Encode(text).Value, "\""));
}
