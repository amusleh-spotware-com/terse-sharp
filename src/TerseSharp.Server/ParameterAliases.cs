using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TerseSharp.Server;

internal sealed record ParameterAlias(string Tool, string Alias, string Canonical, FrozenDictionary<string, bool>? Values = null);

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
            if (!arguments.TryGetValue(alias.Alias, out var value) || arguments.ContainsKey(alias.Canonical) || Converted(alias, value) is not { } converted)
                continue;

            arguments.Remove(alias.Alias);
            arguments[alias.Canonical] = converted;
        }
    }

    public static void Apply(string? tool, JsonObject arguments)
    {
        foreach (var alias in For(tool))
        {
            if (!arguments.TryGetPropertyValue(alias.Alias, out var value) || arguments.ContainsKey(alias.Canonical) || Converted(alias, value) is not { } converted)
                continue;

            arguments.Remove(alias.Alias);
            arguments[alias.Canonical] = converted;
        }
    }

    private static ReadOnlySpan<ParameterAlias> For(string? tool) =>
        tool is not null && ByTool.TryGetValue(tool, out var aliases) ? aliases : [];

    private static JsonElement? Converted(ParameterAlias alias, JsonElement value) => alias.Values switch
    {
        null => value,
        { } values when value.ValueKind is JsonValueKind.String && values.TryGetValue(value.GetString()!, out var flag) => JsonElement.Parse(flag ? "true" : "false"),
        _ => null,
    };

    private static JsonNode? Converted(ParameterAlias alias, JsonNode? value) => (alias.Values, value) switch
    {
        (_, null) => null,
        (null, _) => value,
        ({ } values, JsonValue text) when text.TryGetValue<string>(out var mode) && values.TryGetValue(mode, out var flag) => JsonValue.Create(flag),
        _ => null,
    };
}
