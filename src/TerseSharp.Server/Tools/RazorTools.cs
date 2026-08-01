using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class RazorTools(ToolContext context)
{
    [McpServerTool(Name = "razor_outline")]
    [Description("Directives, components and @code members of a .razor or .cshtml file, each component resolved to its type. Plain HTML elements are hidden unless elements=true. Use instead of Read on a Razor file.")]
    public Task<string> RazorOutline(
        [Description("Path to the .razor or .cshtml file.")] string path,
        [Description("Include plain HTML elements that wire nothing up (default false).")] bool elements = false,
        [Description("Max nodes (200).")] int maxResults = 0,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(
            workspace,
            path,
            async loaded => NavigationTools.Unwrap(
                await RazorService.OutlineAsync(loaded, path, elements, NavigationTools.Cap(maxResults, 200), cancellationToken).ConfigureAwait(false)),
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "razor_component")]
    [Description("How to use a Blazor component: every [Parameter] and [CascadingParameter] with its type, which are [EditorRequired], the routes it declares, and where it comes from - source or a referenced package.")]
    public Task<string> RazorComponent(
        [Description("Component name, e.g. Card or MudBlazor.MudButton.")] string name,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(
            workspace,
            null,
            async loaded => NavigationTools.Unwrap(
                await RazorService.ComponentAsync(loaded, name, cancellationToken).ConfigureAwait(false)),
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "razor_find")]
    [Description("Find components, elements, attributes, directives, expressions or routes across every Razor file in the workspace. Use instead of Grep on .razor or .cshtml.")]
    public Task<string> RazorFind(
        [Description("Text to find.")] string query,
        [Description("What to search: component (default), element, attribute, directive, expression or route.")] string? kind = null,
        [Description("Max results (100).")] int maxResults = 0,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        context.WithWorkspace(workspace, null, loaded => NavigationTools.Unwrap(
            RazorService.Find(loaded, query, kind ?? "component", NavigationTools.Cap(maxResults, 100))));

    [McpServerTool(Name = "razor_bindings")]
    [Description("Every @bind, @on event handler, @ref and asp-for in a Razor file. With validate=true each one is resolved against the component's own type through Roslyn and reported EXACT, NO_SETTER, UNRESOLVED or UNRESOLVED_CONTEXT.")]
    public Task<string> RazorBindings(
        [Description("Path to the .razor or .cshtml file.")] string path,
        [Description("Resolve each binding against the component type (default false).")] bool validate = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(
            workspace,
            path,
            async loaded => NavigationTools.Unwrap(
                await RazorBindingService.BindingsAsync(loaded, path, validate, cancellationToken).ConfigureAwait(false)),
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "razor_codebehind")]
    [Description("The four collocated files of one component - the .razor, its .razor.cs partial, its .razor.css scoped styles and its .razor.js module - plus the _Imports chain and the members declared in @code.")]
    public Task<string> RazorCodeBehind(
        [Description("Path to the .razor or .cshtml file.")] string path,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(
            workspace,
            path,
            async loaded => NavigationTools.Unwrap(
                await RazorService.CodeBehindAsync(loaded, path, cancellationToken).ConfigureAwait(false)),
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "razor_validate")]
    [Description("Report Razor faults the compiler does not catch: unknown component, unknown or missing [Parameter], a @bind with no setter, a route parameter with no property, duplicate @page routes, a bad @ref, an orphan .razor.css, an unregistered @inject, and markup that will not parse.")]
    public Task<string> RazorValidate(
        [Description("Path to the Razor file. Ignored when scope is solution.")] string? path = null,
        [Description("file (default) or solution.")] string? scope = null,
        [Description("Comma-separated rule codes to report, e.g. RZR002,RZR006.")] string? rules = null,
        [Description("Max findings (200).")] int maxResults = 0,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(
            workspace,
            path,
            async loaded => NavigationTools.Unwrap(
                await RazorValidation.RunAsync(
                    loaded,
                    path,
                    scope ?? "file",
                    rules,
                    NavigationTools.Cap(maxResults, 200),
                    cancellationToken).ConfigureAwait(false)),
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "razor_set_attribute")]
    [Description("Set, change or remove one attribute on one element addressed by the path razor_outline prints or by #ref. Formatting is preserved, the result is re-parsed, and the Razor generator re-runs so an edit that breaks the build is rolled back. Use instead of Edit on a Razor file.")]
    public Task<string> RazorSetAttribute(
        [Description("Path to the Razor file.")] string path,
        [Description("Element path from razor_outline, or #ref.")] string target,
        [Description("Attribute name, e.g. Items or @onclick.")] string attribute,
        [Description("Attribute value. Omit to remove the attribute.")] string? value = null,
        [Description("Return the diff without writing.")] bool dryRun = false,
        [Description("Apply even when the edit introduces compile errors.")] bool allowErrors = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.RejectWrite() is { } refusal
            ? Task.FromResult(refusal)
            : context.WithWorkspaceAsync(
                workspace,
                path,
                async loaded => NavigationTools.Unwrap(
                    await RazorEditService.SetAttributeAsync(
                        loaded,
                        path,
                        target,
                        attribute,
                        value,
                        new RazorEditOptions("razor_set_attribute", dryRun, allowErrors),
                        cancellationToken).ConfigureAwait(false)),
                cancellationToken: cancellationToken);

    [McpServerTool(Name = "razor_add_element")]
    [Description("Insert markup relative to one element addressed by the path razor_outline prints or by #ref. Compile-gated through the Razor generator, and refused when the result would not parse.")]
    public Task<string> RazorAddElement(
        [Description("Path to the Razor file.")] string path,
        [Description("Anchor element: path from razor_outline, or #ref.")] string parent,
        [Description("Markup to insert, e.g. <Card Kind=\"warning\" />.")] string markup,
        [Description("last (default), first, before or after.")] string? position = null,
        [Description("Return the diff without writing.")] bool dryRun = false,
        [Description("Apply even when the edit introduces compile errors.")] bool allowErrors = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.RejectWrite() is { } refusal
            ? Task.FromResult(refusal)
            : context.WithWorkspaceAsync(
                workspace,
                path,
                async loaded => NavigationTools.Unwrap(
                    await RazorEditService.AddElementAsync(
                        loaded,
                        path,
                        parent,
                        markup,
                        position ?? "last",
                        new RazorEditOptions("razor_add_element", dryRun, allowErrors),
                        cancellationToken).ConfigureAwait(false)),
                cancellationToken: cancellationToken);

    [McpServerTool(Name = "razor_remove_element")]
    [Description("Remove one element and its children, addressed by the path razor_outline prints or by #ref. Compile-gated through the Razor generator.")]
    public Task<string> RazorRemoveElement(
        [Description("Path to the Razor file.")] string path,
        [Description("Element: path from razor_outline, or #ref.")] string target,
        [Description("Return the diff without writing.")] bool dryRun = false,
        [Description("Apply even when the edit introduces compile errors.")] bool allowErrors = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.RejectWrite() is { } refusal
            ? Task.FromResult(refusal)
            : context.WithWorkspaceAsync(
                workspace,
                path,
                async loaded => NavigationTools.Unwrap(
                    await RazorEditService.RemoveElementAsync(
                        loaded,
                        path,
                        target,
                        new RazorEditOptions("razor_remove_element", dryRun, allowErrors),
                        cancellationToken).ConfigureAwait(false)),
                cancellationToken: cancellationToken);

    [McpServerTool(Name = "razor_set_directive")]
    [Description("Add, replace or remove a file-level directive - @page, @using, @inject, @rendermode, @attribute, @implements, @layout, @typeparam, @model. Compile-gated through the Razor generator.")]
    public Task<string> RazorSetDirective(
        [Description("Path to the Razor file.")] string path,
        [Description("Directive name without the @, e.g. using or rendermode.")] string directive,
        [Description("Directive value. Omit together with remove=true to delete it.")] string? value = null,
        [Description("Remove the directive instead of setting it.")] bool remove = false,
        [Description("Return the diff without writing.")] bool dryRun = false,
        [Description("Apply even when the edit introduces compile errors.")] bool allowErrors = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.RejectWrite() is { } refusal
            ? Task.FromResult(refusal)
            : context.WithWorkspaceAsync(
                workspace,
                path,
                async loaded => NavigationTools.Unwrap(
                    await RazorEditService.SetDirectiveAsync(
                        loaded,
                        path,
                        directive,
                        value,
                        remove,
                        new RazorEditOptions("razor_set_directive", dryRun, allowErrors),
                        cancellationToken).ConfigureAwait(false)),
                cancellationToken: cancellationToken);
}
