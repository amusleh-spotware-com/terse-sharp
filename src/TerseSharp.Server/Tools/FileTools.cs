using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class FileTools(ToolContext context)
{
    [McpServerTool(Name = "read_text")]
    [Description("Read any file, line-ranged. Use for non-C# files; for a .cs file prefer get_file_outline or get_symbol_source. On markdown, headings=true returns the heading map with line ranges and section=\"## Commands\" returns just that section.")]
    public Task<string> ReadText(
        [Description("Path, absolute or workspace-relative.")] string path,
        [Description("First line, 1-based. 0 = start of file.")] int startLine = 0,
        [Description("Last line, 1-based. 0 = end of file.")] int endLine = 0,
        [Description("Maximum lines returned, default 2000. The response is truncated, never refused.")] int maxLines = 0,
        [Description("Markdown only: return the heading map (line ranges, no body) instead of the text.")] bool headings = false,
        [Description("Markdown only: return only this section, e.g. '## Commands'. The heading level is optional.")] string? section = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(
            workspace,
            path,
            async loaded => NavigationTools.Unwrap(await FileService.ReadTextAsync(
                loaded,
                path,
                new FileService.ReadRequest(new FileService.LineRange(startLine, endLine, Lines(maxLines)), headings, section),
                cancellationToken).ConfigureAwait(false)),
            semantic: false,
            cancellationToken);

    [McpServerTool(Name = "write_text")]
    [Description("Create or overwrite a file atomically. A successful write answers in one line - path and changedLines; pass verbose=true for the diff. A .cs file needs force=true, and when it is already a document in the workspace the write is compile-gated exactly like replace_symbol - rolled back if it introduces an error, unless allowErrors=true. Missing directories are created, the file's existing line endings are kept, and the new or changed file is visible to every semantic tool on the next call, with no reload.")]
    public Task<string> WriteText(
        [Description("Path, absolute or workspace-relative.")] string path,
        [Description("Full new content.")] string content,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Allow writing a .cs file. The write is still compile-gated when the file is already in the workspace.")] bool force = false,
        [Description("Apply a .cs write even if it introduces compile errors.")] bool allowErrors = false,
        [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        Guarded(
            workspace,
            path,
            async loaded => NavigationTools.Unwrap(
                await FileService.WriteTextAsync(loaded, path, content, dryRun, force, allowErrors, verbose, cancellationToken).ConfigureAwait(false)));

    [McpServerTool(Name = "edit_text")]
    [Description("Replace a unique snippet in a file, or a whole markdown section with section=\"## Commands\". Line endings are normalized before matching, so a CRLF file accepts an LF oldText. Refuses when the match is not unique and names the file's closest lines. A successful edit answers in one line - path and changedLines; pass verbose=true for the diff.")]
    public Task<string> EditText(
        [Description("Path, absolute or workspace-relative.")] string path,
        [Description("Replacement text. With section=, this is the whole new section including its heading line.")] string newText,
        [Description("Exact text to replace; must occur exactly once. Omit when section= is passed.")] string? oldText = null,
        [Description("Markdown only: replace this whole section, e.g. '## Commands'. No oldText needed.")] string? section = null,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Allow editing a .cs file, bypassing the compile-gated symbol tools. Default false.")] bool force = false,
        [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        Guarded(
            workspace,
            path,
            async loaded => NavigationTools.Unwrap(await FileService.EditTextAsync(
                loaded,
                path,
                new FileService.EditRequest(oldText ?? string.Empty, newText, section, dryRun, force, verbose),
                cancellationToken).ConfigureAwait(false)));

    [McpServerTool(Name = "find_files")]
    [Description("Locate files by glob under the workspace root. Use instead of Glob; bin, obj, .git and node_modules are excluded.")]
    public Task<string> FindFiles(
        [Description("Glob such as *.csproj, *Tests.cs, or a path glob like **/Views/*.xaml. ** spans directories, * and ? stop at a separator.")] string glob,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0) =>
        context.WithWorkspace(
            workspace,
            null,
            loaded => TextSearchService.FindFiles(loaded, glob, NavigationTools.Cap(maxResults, 100)),
            semantic: false);

    [McpServerTool(Name = "search_text")]
    [Description("Literal text search across the workspace. Results are tagged HEURISTIC: for a type or member name use search_symbols or find_usages instead.")]
    public Task<string> SearchText(
        [Description("Literal text to find.")] string pattern,
        [Description("Optional file glob, e.g. *.json or **/Views/*.xaml. ** spans directories, * and ? stop at a separator.")] string? glob = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(
            workspace,
            null,
            loaded => TextSearchService.SearchAsync(loaded, pattern, glob ?? "*", regex: false, NavigationTools.Cap(maxResults, 100), cancellationToken),
            semantic: false,
            cancellationToken);

    [McpServerTool(Name = "search_regex")]
    [Description("Regular-expression search across the workspace. Results are tagged HEURISTIC.")]
    public Task<string> SearchRegex(
        [Description(".NET regular expression.")] string pattern,
        [Description("Optional file glob, e.g. *.cs or **/Views/*.xaml. ** spans directories, * and ? stop at a separator.")] string? glob = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(
            workspace,
            null,
            loaded => TextSearchService.SearchAsync(loaded, pattern, glob ?? "*", regex: true, NavigationTools.Cap(maxResults, 100), cancellationToken),
            semantic: false,
            cancellationToken);

    private Task<string> Guarded(string? workspace, string path, Func<LoadedWorkspace, Task<string>> action) =>
        context.RejectWrite() is { } rejection
            ? Task.FromResult(rejection)
            : context.WithWorkspaceAsync(workspace, path, action, semantic: false);

    private static int Lines(int requested) => requested <= 0 ? 2000 : Math.Min(requested, 20000);
}
