using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class FileTools(ToolContext context)
{
    [McpServerTool(Name = "read_text")]
    [Description("Read any file, line-ranged. Use for non-C# files; for a .cs file prefer get_file_outline or get_symbol_source.")]
    public Task<string> ReadText(
        [Description("File path, absolute or relative to the workspace root.")] string path,
        [Description("First line, 1-based. 0 = start of file.")] int startLine = 0,
        [Description("Last line, 1-based. 0 = end of file.")] int endLine = 0,
        [Description("Maximum lines returned, default 2000. The response is truncated, never refused.")] int maxLines = 0,
        [Description("Optional workspace path or worktree name.")] string? workspace = null) =>
        context.WithWorkspace(workspace, path, loaded =>
            NavigationTools.Unwrap(FileService.ReadText(loaded, path, startLine, endLine, Lines(maxLines))));

    [McpServerTool(Name = "write_text")]
    [Description("Create or overwrite a non-C# file atomically. Returns the diff, not the file.")]
    public Task<string> WriteText(
        [Description("File path, absolute or relative to the workspace root.")] string path,
        [Description("Full new content.")] string content,
        [Description("Return the diff without writing. Default false.")] bool dryRun = false,
        [Description("Allow writing a .cs file, bypassing the compile-gated symbol tools. Default false.")] bool force = false,
        [Description("Optional workspace path or worktree name.")] string? workspace = null) =>
        Guarded(workspace, path, loaded => NavigationTools.Unwrap(FileService.WriteText(loaded, path, content, dryRun, force)));

    [McpServerTool(Name = "edit_text")]
    [Description("Replace an exact unique snippet in a file. Refuses when the match is not unique. Returns the diff.")]
    public Task<string> EditText(
        [Description("File path, absolute or relative to the workspace root.")] string path,
        [Description("Exact text to replace; must occur exactly once.")] string oldText,
        [Description("Replacement text.")] string newText,
        [Description("Return the diff without writing. Default false.")] bool dryRun = false,
        [Description("Allow editing a .cs file, bypassing the compile-gated symbol tools. Default false.")] bool force = false,
        [Description("Optional workspace path or worktree name.")] string? workspace = null) =>
        Guarded(workspace, path, loaded => NavigationTools.Unwrap(FileService.EditText(loaded, path, oldText, newText, dryRun, force)));

    [McpServerTool(Name = "find_files")]
    [Description("Locate files by glob under the workspace root. Use instead of Glob; bin, obj, .git and node_modules are excluded.")]
    public Task<string> FindFiles(
        [Description("Glob such as *.csproj, *Tests.cs, or a path glob like **/Views/*.xaml. ** spans directories, * and ? stop at a separator.")] string glob,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        [Description("Maximum results, default 100.")] int maxResults = 0) =>
        context.WithWorkspace(workspace, null, loaded =>
            TextSearchService.FindFiles(loaded, glob, NavigationTools.Cap(maxResults, 100)));

    [McpServerTool(Name = "search_text")]
    [Description("Literal text search across the workspace. Results are tagged HEURISTIC: for a type or member name use search_symbols or find_usages instead.")]
    public Task<string> SearchText(
        [Description("Literal text to find.")] string pattern,
        [Description("Optional file glob, e.g. *.json or **/Views/*.xaml. ** spans directories, * and ? stop at a separator.")] string? glob = null,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        [Description("Maximum results, default 100.")] int maxResults = 0) =>
        context.WithWorkspace(workspace, null, loaded =>
            TextSearchService.Search(loaded, pattern, glob ?? "*", regex: false, NavigationTools.Cap(maxResults, 100)));

    [McpServerTool(Name = "search_regex")]
    [Description("Regular-expression search across the workspace. Results are tagged HEURISTIC.")]
    public Task<string> SearchRegex(
        [Description(".NET regular expression.")] string pattern,
        [Description("Optional file glob, e.g. *.cs or **/Views/*.xaml. ** spans directories, * and ? stop at a separator.")] string? glob = null,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        [Description("Maximum results, default 100.")] int maxResults = 0) =>
        context.WithWorkspace(workspace, null, loaded =>
            TextSearchService.Search(loaded, pattern, glob ?? "*", regex: true, NavigationTools.Cap(maxResults, 100)));

    private Task<string> Guarded(string? workspace, string path, Func<LoadedWorkspace, string> action) =>
        context.RejectWrite() is { } rejection
            ? Task.FromResult(rejection)
            : context.WithWorkspace(workspace, path, action);

    private static int Lines(int requested) => requested <= 0 ? 2000 : Math.Min(requested, 20000);
}
