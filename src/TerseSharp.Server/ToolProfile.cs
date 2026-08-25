using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TerseSharp.Server;

public readonly record struct ToolSurface(IReadOnlySet<string>? Advertised, bool MarkupDerived, ToolOverrides? Overrides = null);

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

    public static bool Advertises(ToolSurface surface, WorkspaceMarkup served, string tool) =>
        surface.Overrides?.Decision(tool)
            ?? ((surface.Advertised?.Contains(tool) ?? true) && served.Serves(tool));

    public static string? Describe(ToolSurface surface, WorkspaceMarkup markup) =>
            surface.Overrides is { Configured: true } overrides
                ? Joined(Narrowed(overrides) + Ignored(overrides), Derived(surface, markup, counted: false)) + Answers
                : Derived(surface, markup, counted: true) is { } derived ? derived + Answers : null;

    public static McpRequestFilter<ListToolsRequestParams, ListToolsResult> Filter(ToolSurface surface, ToolContext context) =>
        next => async (request, cancellationToken) =>
        {
            var listed = await next(request, cancellationToken).ConfigureAwait(false);
            var served = surface.MarkupDerived
                ? await context.ServedAsync(cancellationToken).ConfigureAwait(false)
                : WorkspaceMarkup.Every;

            listed.Tools = [.. listed.Tools.Where(tool => Advertises(surface, served, tool.Name))];

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

    private static string Located(ToolOverrides overrides) => overrides.Path ?? ToolSettings.FileName;

    private static string Joined(string overridden, string? derived) =>
            derived is { Length: > 0 } ? overridden + "; " + derived : overridden;

    private const string Answers = "; every other tool still answers when called by name";

    private static string Narrowed(ToolOverrides overrides) => overrides switch
    {
        { Failure: { } failure } => string.Create(
            CultureInfo.InvariantCulture,
            $"tools={Located(overrides)} could not be read - {failure}; it narrows nothing"),
        { Hidden: 0 } => "tools=" + Located(overrides) + " hides nothing",
        _ => string.Create(
            CultureInfo.InvariantCulture,
            $"tools={Located(overrides)} - {overrides.Hidden} hidden ({string.Join(", ", overrides.Off)})"),
    };

    private static string Ignored(ToolOverrides overrides) => overrides.Ignored.IsEmpty
        ? string.Empty
        : "; ignored " + string.Join(", ", overrides.Ignored);

    private static string? Derived(ToolSurface surface, WorkspaceMarkup markup, bool counted)
    {
        if (surface.Advertised is { } advertised)
        {
            return counted
                ? string.Create(CultureInfo.InvariantCulture, $"tools={Core} - {advertised.Count} advertised")
                : "tools=" + Core;
        }

        return surface.MarkupDerived && !markup.Complete
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"tools={markup.Hidden()} hidden - this workspace holds no such file")
            : null;
    }
}
