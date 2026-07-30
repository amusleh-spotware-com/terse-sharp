using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class XamlTools(ToolContext context)
{
    [McpServerTool(Name = "xaml_outline")]
    [Description("Element tree of a XAML file with x:Name, x:Key and line numbers, without the attributes. Use instead of Read on a .xaml file.")]
    public string XamlOutline(
        [Description("Path to the .xaml, .axaml or .paml file.")] string path,
        [Description("Maximum nesting depth to show, default 4.")] int depth = 0,
        [Description("Optional workspace path or worktree name.")] string? workspace = null) =>
        context.WithWorkspace(workspace, path, loaded =>
            NavigationTools.Unwrap(XamlService.Outline(loaded, path, depth <= 0 ? 4 : depth)));

    [McpServerTool(Name = "xaml_names")]
    [Description("Every x:Name in a XAML file with its element type and location.")]
    public string XamlNames(
        [Description("Path to the XAML file.")] string path,
        [Description("Optional workspace path or worktree name.")] string? workspace = null) =>
        context.WithWorkspace(workspace, path, loaded => NavigationTools.Unwrap(XamlService.Names(loaded, path)));

    [McpServerTool(Name = "xaml_resources")]
    [Description("Every x:Key resource declared in a XAML file with its type and location.")]
    public string XamlResources(
        [Description("Path to the XAML file.")] string path,
        [Description("Optional workspace path or worktree name.")] string? workspace = null) =>
        context.WithWorkspace(workspace, path, loaded => NavigationTools.Unwrap(XamlService.Resources(loaded, path)));

    [McpServerTool(Name = "xaml_bindings")]
    [Description("Every binding expression in a XAML file - Binding, CompiledBinding and x:Bind - with the element and property it sits on.")]
    public string XamlBindings(
        [Description("Path to the XAML file.")] string path,
        [Description("Optional workspace path or worktree name.")] string? workspace = null) =>
        context.WithWorkspace(workspace, path, loaded => NavigationTools.Unwrap(XamlService.Bindings(loaded, path)));

    [McpServerTool(Name = "xaml_validate")]
    [Description("Check a XAML file for well-formedness, duplicate x:Key and x:Name, and StaticResource references that resolve to nothing in the file.")]
    public string XamlValidate(
        [Description("Path to the XAML file.")] string path,
        [Description("Optional workspace path or worktree name.")] string? workspace = null) =>
        context.WithWorkspace(workspace, path, loaded => NavigationTools.Unwrap(XamlService.Validate(loaded, path)));

    [McpServerTool(Name = "xaml_find")]
    [Description("Find XAML elements across the workspace by element type, x:Name, resource key or binding text. Use instead of Grep over .xaml files.")]
    public string XamlFind(
        [Description("Value to look for.")] string query,
        [Description("What to match: type (default), name, resource or binding.")] string? kind = null,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        [Description("Maximum results, default 100.")] int maxResults = 0) =>
        context.WithWorkspace(workspace, null, loaded =>
            NavigationTools.Unwrap(XamlService.Find(loaded, query, kind ?? "type", NavigationTools.Cap(maxResults, 100))));
}
