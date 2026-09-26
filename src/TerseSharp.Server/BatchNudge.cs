using System.Text.Json;
using System.Text.Json.Nodes;

namespace TerseSharp.Server;

public static class BatchNudge
{
    internal const string Nudge =
        "That response carried ONE read-only terse call. Before the next one, list everything you still need, and request every item that does not depend on another's result in ONE response - independent calls in one message run concurrently, and each call sent alone pays a full model round trip.";

    private static readonly string[] BatchKeys = ["tool_calls", "tool_uses", "tools", "batch", "calls"];

    private static readonly HashSet<string> ReadTools = new(StringComparer.Ordinal)
    {
        "changed_files", "diff_symbols", "diff_text", "find_files", "find_implementations", "find_registrations",
        "find_usages", "get_diagnostics", "get_file_outline", "get_symbol", "get_symbol_source", "get_type_outline",
        "history", "list_endpoints", "list_projects", "list_workspaces", "package_list", "project_properties",
        "read_text", "search_regex", "search_symbols", "search_text", "solution_projects", "workspace_status",
        "resx_files", "resx_find", "resx_get", "resx_usages",
        "xaml_bindings", "xaml_codebehind", "xaml_find", "xaml_localization", "xaml_names", "xaml_outline",
        "xaml_resolve", "xaml_resources", "xaml_styles",
        "razor_bindings", "razor_codebehind", "razor_component", "razor_find", "razor_outline",
    };

    internal static IReadOnlyCollection<string> Reads => ReadTools;

    public static async Task<int> RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        var payload = await input.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        if (Decide(payload) is { } context)
            await output.WriteLineAsync(context).ConfigureAwait(false);

        return 0;
    }

    internal static string? Decide(string payload)
    {
        try
        {
            return JsonNode.Parse(payload) is JsonObject root && Single(root) is { } name && IsTerseRead(name)
                ? Rendered()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Single(JsonObject root)
    {
        foreach (var key in BatchKeys)
        {
            if (root[key] is JsonArray calls)
                return calls.Count is 1 && calls[0] is JsonObject call ? Named(call) : null;
        }

        return null;
    }

    private static string? Named(JsonObject call) =>
        (call["tool_name"] ?? call["name"]) is JsonValue value && value.TryGetValue(out string? name) ? name : null;

    private static bool IsTerseRead(ReadOnlySpan<char> name)
    {
        if (!name.StartsWith("mcp__", StringComparison.Ordinal))
            return false;

        var qualified = name[5..];
        var split = qualified.LastIndexOf("__", StringComparison.Ordinal);

        return split > 0
            && qualified[..split].Contains("terse", StringComparison.OrdinalIgnoreCase)
            && ReadTools.GetAlternateLookup<ReadOnlySpan<char>>().Contains(qualified[(split + 2)..]);
    }

    private static string Rendered() => new JsonObject
    {
        ["hookSpecificOutput"] = new JsonObject
        {
            ["hookEventName"] = "PostToolBatch",
            ["additionalContext"] = Nudge,
        },
    }.ToJsonString();
}
