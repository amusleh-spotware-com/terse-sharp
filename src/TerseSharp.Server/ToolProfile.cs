using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TerseSharp.Server;

public readonly record struct ToolSurface(IReadOnlySet<string>? Advertised, bool MarkupDerived);

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

    public static ToolSurface Resolve(string? requested) =>
        Resolve(requested, Environment.GetEnvironmentVariable("TERSE_TOOLS"));

    public static ToolSurface Resolve(string? requested, string? environment)
    {
        var name = requested ?? environment;

        if (string.IsNullOrEmpty(name))
            return new(null, MarkupDerived: true);

        if (string.Equals(name, Core, StringComparison.OrdinalIgnoreCase))
            return new(CoreTools, MarkupDerived: false);

        if (string.Equals(name, All, StringComparison.OrdinalIgnoreCase))
            return new(null, MarkupDerived: false);

        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"terse: --tools '{name}' is not a profile; advertising the families this workspace holds. Use core or all."));

        return new(null, MarkupDerived: true);
    }

    public static string? Describe(ToolSurface surface, WorkspaceMarkup markup)
    {
        if (surface.Advertised is { } advertised)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"tools={Core} - {advertised.Count} advertised; every other tool still answers when called by name");
        }

        return surface.MarkupDerived && !markup.Complete
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"tools={markup.Hidden()} hidden - this workspace holds no such file; every other tool still answers when called by name")
            : null;
    }

    public static McpRequestFilter<ListToolsRequestParams, ListToolsResult> Filter(IReadOnlySet<string> advertised) =>
        next => async (request, cancellationToken) =>
        {
            var listed = await next(request, cancellationToken).ConfigureAwait(false);

            listed.Tools = [.. listed.Tools.Where(tool => advertised.Contains(tool.Name))];

            return listed;
        };

    public static McpRequestFilter<ListToolsRequestParams, ListToolsResult> MarkupFilter(ToolContext context) =>
    next => async (request, cancellationToken) =>
    {
        var listed = await next(request, cancellationToken).ConfigureAwait(false);
        var served = await context.ServedAsync(cancellationToken).ConfigureAwait(false);

        listed.Tools = [.. listed.Tools.Where(tool => served.Serves(tool.Name))];

        return listed;
    };

    public static WorkspaceMarkup Served(WorkspaceRegistry registry)
    {
        var loaded = registry.All();

        if (loaded.Count is 0)
            return WorkspaceMarkup.Every;

        var served = default(WorkspaceMarkup);

        foreach (var workspace in loaded)
            served = served.Union(workspace.Indexes.MarkupFamilies());

        return served;
    }
}
