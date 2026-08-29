using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
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
        ["add_member"] = "declarations",
        ["search_text"] = "queries",
        ["search_regex"] = "queries",
        ["run_tests"] = "projects",
        ["find_files"] = "globs",
        ["get_type_outline"] = "symbolIds",
        ["write_text"] = "files",
        ["edit_text"] = "edits",
        ["resx_set"] = "entries",
        ["analyze"] = "paths",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly Lock Gate = new();

    private static string last = string.Empty;
    private static int run;

    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Filter() => next => async (request, cancellationToken) =>
{
    var result = await next(request, cancellationToken).ConfigureAwait(false);

    if (request.Params is not { Name.Length: > 0 } parameters)
        return result;

    if (IdenticalCall.Note(parameters.Name, parameters, result) is { } repeat)
        result.Content.Add(new TextContentBlock { Text = repeat });

    if (Steer(parameters.Name, Batched(parameters, parameters.Name), Unbatchable(parameters, parameters.Name), Argument(parameters, parameters.Name)) is { } note)
        result.Content.Add(new TextContentBlock { Text = note });

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

    public static string? Steer(string tool, bool batched = false, bool unbatchable = false, string? value = null)
    {
        if (unbatchable)
        {
            Forget();

            return null;
        }

        var (count, seen, captured) = Counted(tool, value);

        return Repeated(tool, count, seen, captured, batched);
    }

    private static string? Repeated(string tool, int count, string[] seen, int captured, bool batched)
    {
        if (batched || count < Threshold || !Plural.TryGetValue(tool, out var plural))
            return null;

        return Concrete(seen, count, captured) is { } filled
            ? string.Create(CultureInfo.InvariantCulture, $"{count} {tool} calls in a row - these are ONE call: {plural}=[{filled}]")
            : string.Create(CultureInfo.InvariantCulture, $"{count} {tool} calls in a row - pass {plural}=[...] with the next {Math.Min(count, MaxBatch)}+ in ONE call");
    }

    private const int MaxBatch = 10;

    public static void Forget()
    {
        lock (Gate)
        {
            last = string.Empty;
            run = 0;
            captured = 0;
            Values.Clear();
        }
    }

    private static (int Count, string[] Seen, int Captured) Counted(string tool, string? value)
    {
        lock (Gate)
        {
            if (!string.Equals(last, tool, StringComparison.Ordinal))
            {
                run = 0;
                captured = 0;
                Values.Clear();
            }

            run++;
            last = tool;

            if (value is { Length: > 0 })
            {
                captured++;

                if (Values.Count < MaxBatch && !Values.Contains(value, StringComparer.Ordinal))
                    Values.Add(value);
            }

            return (run, [.. Values], captured);
        }
    }

    private const int MaxValueLength = 80;
    private static readonly List<string> Values = new(MaxBatch);
    public static readonly FrozenDictionary<string, string> Singular = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["read_text"] = "path",
        ["get_file_outline"] = "path",
        ["diff_text"] = "path",
        ["get_symbol_source"] = "symbolId",
        ["replace_symbol"] = "symbolId",
        ["search_text"] = "query",
        ["search_regex"] = "query",
        ["run_tests"] = "project",
        ["find_files"] = "glob",
        ["get_type_outline"] = "symbolId",
        ["analyze"] = "path",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static string? Argument(CallToolRequestParams parameters, string tool)
    {
        if (!Singular.TryGetValue(tool, out var name) || parameters.Arguments is not { } arguments)
            return null;

        if (!arguments.TryGetValue(name, out var element)
            && !(string.Equals(name, "symbolId", StringComparison.Ordinal) && arguments.TryGetValue("symbol", out element)))
        {
            return null;
        }

        return element.ValueKind is JsonValueKind.String ? element.GetString() : null;
    }

    private static string? Concrete(string[] seen, int count, int captured)
    {
        if (captured != count || seen.Length < Threshold)
            return null;

        var builder = new StringBuilder();

        foreach (var value in seen)
        {
            if (value.Length > MaxValueLength || builder.Length > MaxSteerLength)
                return null;

            if (builder.Length > 0)
                builder.Append(", ");

            builder.Append('"').Append(value.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
        }

        return builder.ToString();
    }

    private const int MaxSteerLength = 120;
    private static int captured;
}
