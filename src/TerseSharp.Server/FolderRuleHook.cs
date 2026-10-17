using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using TerseSharp.Server.Tools;

namespace TerseSharp.Server;

internal sealed record FolderRuleHook(string Event, string Session, string Cwd, string? Tool, JsonObject Payload)
{
    private const string DefaultSession = "default";

    private static readonly string[] PathKeys = ["path", "toPath", "file_path"];

    private static readonly string[] ItemKeys = ["files", "edits"];

    private static readonly FrozenSet<string> AddressingParameters = FrozenSet.Create(StringComparer.Ordinal, "path", "paths", "toPath", "files", "edits");

    private static readonly Type[] MaintenanceTools = [typeof(AnalysisTools), typeof(BuildTools)];

    private JsonObject Input => Payload["tool_input"] as JsonObject ?? [];

    internal static FolderRuleHook? Parse(string payload)
    {
        try
        {
            return JsonNode.Parse(payload) is JsonObject root ? From(root) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static FrozenSet<string> WritingTools() =>
        typeof(FolderRuleHook).Assembly.GetTypes()
            .Where(type => type.IsDefined(typeof(McpServerToolTypeAttribute)) && !MaintenanceTools.Contains(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(WritesToAPath)
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .OfType<string>()
            .ToFrozenSet(StringComparer.Ordinal);

    internal IEnumerable<string> Targets() =>
        PathKeys.Select(key => Text(Input[key]))
            .Concat((Input["paths"] as JsonArray)?.Select(Text) ?? [])
            .Concat(ItemKeys.SelectMany(key => (Input[key] as JsonArray)?.OfType<JsonObject>().Select(item => Text(item["path"])) ?? []))
            .OfType<string>();

    internal List<string> Mentioned() => ResponsePaths.Mentioned(Payload["tool_response"]);

    internal IReadOnlyList<string> Bases() => FolderRuleBases.For(Cwd, Text(Input["workspace"]));

    private static FolderRuleHook? From(JsonObject root) =>
        Text(root["hook_event_name"]) is { } hookEvent && Text(root["cwd"]) is { Length: > 0 } cwd
            ? new(hookEvent, SessionOf(root), cwd, TerseTool(Text(root["tool_name"])), root)
            : null;

    private static string SessionOf(JsonObject root) =>
    (Text(root["session_id"]), Text(root["agent_id"])) switch
    {
        ({ Length: > 0 } session, { Length: > 0 } agent) => string.Concat(session, "-", agent),
        ({ Length: > 0 } session, _) => session,
        _ => DefaultSession,
    };

    private static string? TerseTool(string? name)
    {
        if (name is null || !name.StartsWith("mcp__", StringComparison.Ordinal))
            return null;

        var qualified = name.AsSpan(5);
        var split = qualified.LastIndexOf("__", StringComparison.Ordinal);

        return split > 0 && qualified[..split].Contains("terse", StringComparison.OrdinalIgnoreCase) ? qualified[(split + 2)..].ToString() : null;
    }

    private static bool WritesToAPath(MethodInfo method)
    {
        var parameters = method.GetParameters();

        return parameters.Any(parameter => parameter.Name is "dryRun") && parameters.Any(parameter => parameter.Name is { } name && AddressingParameters.Contains(name));
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    internal bool DryRun => Input["dryRun"] is JsonValue value && value.TryGetValue(out bool dryRun) && dryRun;
}
