using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class XamlTools(ToolContext context)
{
    [McpServerTool(Name = "xaml_outline")]
    [Description("Element tree of a XAML file with x:Name, x:Key and line numbers, without the attributes. Use instead of Read on a .xaml file.")]
    public Task<string> XamlOutline(
        [Description("Path to the .xaml, .axaml or .paml file.")] string path,
        [Description("Maximum nesting depth to show, default 4.")] int depth = 0,
        [Description("Which elements to list: all (default), named or keyed.")] string? filter = null,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        context.WithWorkspace(workspace, path, loaded =>
            NavigationTools.Unwrap(XamlService.Outline(loaded, path, depth <= 0 ? 4 : depth, filter ?? "all")));

    [McpServerTool(Name = "xaml_names")]
    [Description("Every x:Name and x:Uid in a XAML file with its element type and location.")]
    public Task<string> XamlNames(
        [Description("Path to the XAML file.")] string path,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        context.WithWorkspace(workspace, path, loaded => NavigationTools.Unwrap(XamlService.Names(loaded, path)));

    [McpServerTool(Name = "xaml_resources")]
    [Description("Every x:Key resource declared in a XAML file with its type and location.")]
    public Task<string> XamlResources(
        [Description("Path to the XAML file.")] string path,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        context.WithWorkspace(workspace, path, loaded => NavigationTools.Unwrap(XamlService.Resources(loaded, path)));

    [McpServerTool(Name = "xaml_resolve")]
    [Description("Where a StaticResource, DynamicResource or ThemeResource key is declared across every XAML file in the workspace, with each declaration's scope. Use instead of reading App.xaml and every merged dictionary.")]
    public Task<string> XamlResolve(
        [Description("The x:Key to resolve.")] string key,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        context.WithWorkspace(workspace, null, loaded => NavigationTools.Unwrap(XamlService.Resolve(loaded, key)));

    [McpServerTool(Name = "xaml_bindings")]
    [Description("Every binding expression in a XAML file - Binding, CompiledBinding and x:Bind - with the element and property it sits on. With validate=true each path is checked against the x:DataType or d:DataContext type resolved through Roslyn.")]
    public Task<string> XamlBindings(
        [Description("Path to the XAML file.")] string path,
        [Description("Check each binding path against its data-context type (default false).")] bool validate = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        validate
            ? context.WithWorkspaceAsync(workspace, path, async loaded =>
                NavigationTools.Unwrap(await XamlService.ValidateBindingsAsync(loaded, path, cancellationToken).ConfigureAwait(false)))
            : context.WithWorkspace(workspace, path, loaded => NavigationTools.Unwrap(XamlService.Bindings(loaded, path)));

    [McpServerTool(Name = "xaml_validate")]
    [Description("Check XAML for well-formedness, duplicate x:Key and x:Name, and resource references that resolve to no declaration anywhere in the workspace. Pass scope=solution to check every XAML file.")]
    public Task<string> XamlValidate(
        [Description("Path to the XAML file. Ignored when scope is solution.")] string? path = null,
        [Description("file (default) or solution.")] string? scope = null,
        [Description("Max issues when scope is solution (200).")] int maxResults = 0,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        string.Equals(scope, "solution", StringComparison.OrdinalIgnoreCase)
            ? context.WithWorkspace(workspace, null, loaded =>
                NavigationTools.Unwrap(XamlService.ValidateAll(loaded, NavigationTools.Cap(maxResults, 200))))
            : context.WithWorkspace(workspace, path, loaded =>
                NavigationTools.Unwrap(XamlService.Validate(loaded, path ?? string.Empty)));

    [McpServerTool(Name = "xaml_find")]
    [Description("Find XAML elements across the workspace by element type, x:Name, resource key, x:Uid or binding text. Use instead of Grep over .xaml files.")]
    public Task<string> XamlFind(
        [Description("Value to look for.")] string query,
        [Description("What to match: type (default), name, resource, uid or binding.")] string? kind = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0) =>
        context.WithWorkspace(workspace, null, loaded =>
            NavigationTools.Unwrap(XamlService.Find(loaded, query, kind ?? "type", NavigationTools.Cap(maxResults, 100))));
}
