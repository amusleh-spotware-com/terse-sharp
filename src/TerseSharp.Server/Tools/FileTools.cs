using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class FileTools(ToolContext context)
{
    [McpServerTool(Name = "read_text")]
    [Description("Read any file, line-ranged. Use for non-C# files; for a .cs file prefer get_file_outline or get_symbol_source.")]
    public Task<string> ReadText(
        [Description("Path, absolute or workspace-relative.")] string path,
        [Description("First line, 1-based. 0 = start of file.")] int startLine = 0,
        [Description("Last line, 1-based. 0 = end of file.")] int endLine = 0,
        [Description("Maximum lines returned, default 2000. The response is truncated, never refused.")] int maxLines = 0,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspace(
            workspace,
            path,
            loaded => NavigationTools.Unwrap(FileService.ReadText(loaded, path, startLine, endLine, Lines(maxLines), cancellationToken)),
            semantic: false,
            cancellationToken);

    [McpServerTool(Name = "write_text")]
    [Description("Create or overwrite a file atomically. Returns the diff, not the file. A .cs file needs force=true; the new or changed file is visible to every semantic tool on the next call, with no reload.")]
    public Task<string> WriteText(
        [Description("Path, absolute or workspace-relative.")] string path,
        [Description("Full new content.")] string content,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Allow writing a .cs file, bypassing the compile-gated symbol tools. Default false.")] bool force = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Guarded(workspace, path, loaded => NavigationTools.Unwrap(FileService.WriteText(loaded, path, content, dryRun, force)));

    [McpServerTool(Name = "edit_text")]
    [Description("Replace an exact unique snippet in a file. Refuses when the match is not unique. Returns the diff.")]
    public Task<string> EditText(
        [Description("Path, absolute or workspace-relative.")] string path,
        [Description("Exact text to replace; must occur exactly once.")] string oldText,
        [Description("Replacement text.")] string newText,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Allow editing a .cs file, bypassing the compile-gated symbol tools. Default false.")] bool force = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Guarded(workspace, path, loaded => NavigationTools.Unwrap(FileService.EditText(loaded, path, oldText, newText, dryRun, force)));

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
        [Description("Max results (100).")] int maxResults = 0) =>
        context.WithWorkspace(
            workspace,
            null,
            loaded => TextSearchService.Search(loaded, pattern, glob ?? "*", regex: false, NavigationTools.Cap(maxResults, 100)),
            semantic: false);

    [McpServerTool(Name = "search_regex")]
    [Description("Regular-expression search across the workspace. Results are tagged HEURISTIC.")]
    public Task<string> SearchRegex(
        [Description(".NET regular expression.")] string pattern,
        [Description("Optional file glob, e.g. *.cs or **/Views/*.xaml. ** spans directories, * and ? stop at a separator.")] string? glob = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0) =>
        context.WithWorkspace(
            workspace,
            null,
            loaded => TextSearchService.Search(loaded, pattern, glob ?? "*", regex: true, NavigationTools.Cap(maxResults, 100)),
            semantic: false);

    private Task<string> Guarded(string? workspace, string path, Func<LoadedWorkspace, string> action) =>
        context.RejectWrite() is { } rejection
            ? Task.FromResult(rejection)
            : context.WithWorkspace(workspace, path, action, semantic: false);

    private static int Lines(int requested) => requested <= 0 ? 2000 : Math.Min(requested, 20000);
}
