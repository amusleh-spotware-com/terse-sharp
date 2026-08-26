using System.Collections.Frozen;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TerseSharp.Server;

public static class RepeatSteer
{
    public const int Threshold = 2;

    public static readonly FrozenDictionary<string, string> Plural = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["read_text"] = "paths",
        ["get_file_outline"] = "paths",
        ["diff_text"] = "paths",
        ["get_symbol_source"] = "symbolIds",
        ["replace_symbol"] = "symbolIds",
        ["search_text"] = "queries",
        ["search_regex"] = "queries",
        ["run_tests"] = "projects",
        ["write_text"] = "files",
        ["edit_text"] = "edits",
        ["resx_set"] = "entries",
        ["analyze"] = "paths",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly Lock Gate = new();

    private static string last = string.Empty;
    private static int run;

    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Filter() =>
    next => async (request, cancellationToken) =>
    {
        var result = await next(request, cancellationToken).ConfigureAwait(false);

        if (request.Params?.Name is { Length: > 0 } tool
            && Steer(tool, Batched(request.Params, tool), Unbatchable(request.Params, tool)) is { } note)
        {
            result.Content.Add(new TextContentBlock { Text = note });
        }

        return result;
    };

    private static readonly string[] PerEntryOnly = ["startLine", "endLine", "tail", "section"];

    public static bool Unbatchable(CallToolRequestParams parameters, string tool) =>
        string.Equals(tool, "read_text", StringComparison.Ordinal)
        && parameters.Arguments is { } arguments
        && Array.Exists(PerEntryOnly, arguments.ContainsKey);

    private static bool Batched(CallToolRequestParams parameters, string tool) =>
        Plural.TryGetValue(tool, out var plural)
        && parameters.Arguments is { } arguments
        && arguments.ContainsKey(plural);

    public static string? Steer(string tool, bool batched = false, bool unbatchable = false)
    {
        if (unbatchable)
        {
            Forget();

            return null;
        }

        return Repeated(tool, Counted(tool), batched);
    }

    private static string? Repeated(string tool, int count, bool batched) =>
        !batched && count >= Threshold && Plural.TryGetValue(tool, out var plural)
            ? string.Create(CultureInfo.InvariantCulture, $"{count} {tool} calls in a row - pass {plural}=[...] with the next {Math.Min(count, MaxBatch)}+ in ONE call")
            : null;

    private const int MaxBatch = 10;

    public static void Forget()
    {
        lock (Gate)
        {
            last = string.Empty;
            run = 0;
        }
    }

    private static int Counted(string tool)
    {
        lock (Gate)
        {
            run = string.Equals(last, tool, StringComparison.Ordinal) ? run + 1 : 1;
            last = tool;

            return run;
        }
    }
}
