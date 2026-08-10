using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TerseSharp.Server;

public static class ToolProfile
{
    public const string Core = "core";

    public const string All = "all";

    public static readonly IReadOnlySet<string> CoreTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "load_workspace",
        "workspace_status",
        "get_file_outline",
        "get_symbol_source",
        "get_type_outline",
        "search_symbols",
        "find_usages",
        "find_implementations",
        "find_files",
        "search_text",
        "read_text",
        "replace_symbol_body",
        "replace_symbol",
        "add_member",
        "write_text",
        "edit_text",
        "changed_files",
        "diff_symbols",
        "build",
        "run_tests",
        "analyze",
    };

    public static IReadOnlySet<string>? Resolve(string? requested) =>
        Resolve(requested, Environment.GetEnvironmentVariable("TERSE_TOOLS"));

    public static IReadOnlySet<string>? Resolve(string? requested, string? environment)
    {
        var name = requested ?? environment;

        if (string.Equals(name, Core, StringComparison.OrdinalIgnoreCase))
            return CoreTools;

        if (name is { Length: > 0 } && !string.Equals(name, All, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"terse: --tools '{name}' is not a profile; advertising every tool. Use core or all."));
        }

        return null;
    }

    public static string? Describe(IReadOnlySet<string>? advertised) => advertised is null
    ? null
    : string.Create(
        CultureInfo.InvariantCulture,
        $"tools={Core} - {advertised.Count} advertised; every other tool still answers when called by name");

    public static McpRequestFilter<ListToolsRequestParams, ListToolsResult> Filter(IReadOnlySet<string> advertised) =>
        next => async (request, cancellationToken) =>
        {
            var listed = await next(request, cancellationToken).ConfigureAwait(false);

            listed.Tools = [.. listed.Tools.Where(tool => advertised.Contains(tool.Name))];

            return listed;
        };
}
