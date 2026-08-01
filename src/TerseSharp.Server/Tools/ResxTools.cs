using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class ResxTools(ToolContext context)
{
    [McpServerTool(Name = "resx_files")]
    [Description("Every .resx/.resw family in the workspace with its cultures, entry counts, missing translations and designer file. Use instead of Glob plus Read over resource files.")]
    public Task<string> ResxFiles(
        [Description("Optional path fragment to filter the families.")] string? filter = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0) =>
        context.WithWorkspace(workspace, null, loaded =>
            NavigationTools.Unwrap(ResxService.Files(loaded, filter, NavigationTools.Cap(maxResults, 100))));

    [McpServerTool(Name = "resx_get")]
    [Description("Keys of one .resx family with their values per culture. A key missing from a culture is printed MISSING rather than omitted. Use instead of Read on a .resx file.")]
    public Task<string> ResxGet(
        [Description("Path to any file of the family, e.g. src/App/Strings.resx.")] string path,
        [Description("neutral (default), all, or a comma-separated culture list.")] string? cultures = null,
        [Description("Only keys starting with this prefix.")] string? prefix = null,
        [Description("Only this exact key.")] string? key = null,
        [Description("Include the values. false lists the keys only and costs far less.")] bool values = true,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (200).")] int maxResults = 0) =>
        context.WithWorkspace(workspace, path, loaded => NavigationTools.Unwrap(ResxService.Get(
            loaded,
            path,
            cultures ?? "neutral",
            prefix,
            key,
            values,
            NavigationTools.Cap(maxResults, 200))));

    [McpServerTool(Name = "resx_find")]
    [Description("Search every .resx/.resw in the workspace by key, value or comment. Use instead of Grep over resource files.")]
    public Task<string> ResxFind(
        [Description("Text to look for.")] string query,
        [Description("What to match: key (default), value, comment or all.")] string? scope = null,
        [Description("Restrict to one culture, or neutral.")] string? culture = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0) =>
        context.WithWorkspace(workspace, null, loaded => NavigationTools.Unwrap(ResxService.Find(
            loaded,
            query,
            scope ?? "key",
            culture,
            NavigationTools.Cap(maxResults, 100))));

    [McpServerTool(Name = "resx_usages")]
    [Description("Every reference to a resource key: the generated designer property resolved through Roslyn (EXACT), plus GetString, localizer indexers, x:Uid and Razor literals (HEURISTIC). Reports composedLookups so 'no usages' is never claimed as proof when keys are built at runtime.")]
    public Task<string> ResxUsages(
        [Description("The resource key.")] string key,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, null, async loaded => NavigationTools.Unwrap(
            await ResxUsageService
                .UsagesAsync(loaded, key, NavigationTools.Cap(maxResults, 100), cancellationToken)
                .ConfigureAwait(false)));

    [McpServerTool(Name = "resx_set")]
    [Description("Add or update one key, or several as Key=Value lines, in a .resx file. Preserves the file's schema header, ordering, indentation, line endings and byte order mark, and refuses an edit that would produce malformed XML. Use instead of Edit on a .resx file. Not covered by undo_last_change - pass dryRun first if unsure.")]
    public Task<string> ResxSet(
        [Description("Path to the .resx/.resw file, or to any file of its family when culture is given.")] string path,
        [Description("The key to add or update.")] string? key = null,
        [Description("The value for key.")] string? value = null,
        [Description("Several entries, one Key=Value per line. Mutually exclusive with key.")] string? entries = null,
        [Description("Target culture, e.g. fr. Omitted writes the file named by path; a missing culture file is created from the neutral header.")] string? culture = null,
        [Description("Optional comment for the entry.")] string? comment = null,
        [Description("Return the diff without writing.")] bool dryRun = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        context.RejectWrite() is { } refusal
            ? Task.FromResult(refusal)
            : context.WithWorkspace(workspace, path, loaded => NavigationTools.Unwrap(
                ResxEditService.Set(loaded, path, key, value, entries, culture, comment, dryRun)));

    [McpServerTool(Name = "resx_remove")]
    [Description("Remove a key from one culture, or from every file of the family when culture is omitted. Refused while the key is still referenced - by the designer property through Roslyn or by a textual lookup - unless force=true. Not covered by undo_last_change.")]
    public Task<string> ResxRemove(
        [Description("Path to any file of the family.")] string path,
        [Description("The key to remove.")] string key,
        [Description("Only this culture, or neutral. Omitted removes it from every file of the family.")] string? culture = null,
        [Description("Remove even though the key is still referenced.")] bool force = false,
        [Description("Return the diff without writing.")] bool dryRun = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.RejectWrite() is { } refusal
            ? Task.FromResult(refusal)
            : context.WithWorkspaceAsync(workspace, path, async loaded => NavigationTools.Unwrap(
                await ResxEditService
                    .RemoveAsync(loaded, path, key, culture, force, dryRun, cancellationToken)
                    .ConfigureAwait(false)));

    [McpServerTool(Name = "resx_rename")]
    [Description("Rename a key across every file of the family and, unless updateReferences=false, in the C#, XAML and Razor sites that name it. All or nothing: nothing is written if any file would end up malformed. Not covered by undo_last_change.")]
    public Task<string> ResxRename(
        [Description("Path to any file of the family.")] string path,
        [Description("The key to rename.")] string key,
        [Description("The new key.")] string newKey,
        [Description("Also rewrite the references that name the key. Default true.")] bool updateReferences = true,
        [Description("Return the diff without writing.")] bool dryRun = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        context.RejectWrite() is { } refusal
            ? Task.FromResult(refusal)
            : context.WithWorkspace(workspace, path, loaded => NavigationTools.Unwrap(
                ResxEditService.Rename(loaded, path, key, newKey, updateReferences, dryRun)));

    [McpServerTool(Name = "resx_validate")]
    [Description("Lint the resource families: RESX001 missing translation, RESX002 placeholder mismatch, RESX003 unused key, RESX004 duplicate name, RESX005 orphan, RESX006 empty value, RESX007 trimmed whitespace, RESX008 unsorted, RESX009 stale designer. Answers 'which keys are untranslated' without reading a single file.")]
    public Task<string> ResxValidate(
        [Description("Optional path to one family; empty validates the whole workspace.")] string? path = null,
        [Description("Comma-separated rule ids to keep, e.g. RESX001,RESX002.")] string? rules = null,
        [Description("Include RESX003 unused keys. Costs a solution-wide scan and is HEURISTIC.")] bool includeUnused = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (200).")] int maxResults = 0) =>
        context.WithWorkspace(workspace, path, loaded => NavigationTools.Unwrap(
            ResxValidation.Validate(loaded, path, rules, includeUnused, NavigationTools.Cap(maxResults, 200))));
}
