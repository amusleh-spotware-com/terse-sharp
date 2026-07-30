using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class AnalysisTools(ToolContext context)
{
    [McpServerTool(Name = "analyze")]
    [Description("Compiler diagnostics plus every analyzer the project references, deduplicated, down to info severity. Use instead of reading build output; catches dead code, unused usings and style violations the build hides.")]
    public Task<string> Analyze(
        [Description("Optional file path to scope to a single file.")] string? path = null,
        [Description("Minimum severity: error, warning, info, hidden. Default info.")] string? minSeverity = null,
        [Description("Optional comma-separated diagnostic ids to keep, e.g. CA1822,IDE0005.")] string? ids = null,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        [Description("Maximum results, default 200.")] int maxResults = 0,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, path, loaded => AnalysisService.AnalyzeAsync(
            loaded, path, Severity(minSeverity), Split(ids), NavigationTools.Cap(maxResults, 200), cancellationToken));

    [McpServerTool(Name = "format")]
    [Description("Reformat C# to the project's .editorconfig using the Roslyn formatter. Returns the diff, never the file.")]
    public Task<string> Format(
        [Description("Optional file path. Empty formats every document in the workspace.")] string? path = null,
        [Description("Return the diff without writing. Default false.")] bool dryRun = false,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, path, loaded => FormatService.FormatAsync(
            loaded, path, new EditOptions("format", dryRun, AllowErrors: false), cancellationToken));

    [McpServerTool(Name = "cleanup")]
    [Description("Remove unused using directives, sort the remaining ones System-first, then reformat. Returns the diff and is rolled back if it breaks the build.")]
    public Task<string> Cleanup(
        [Description("Optional file path. Empty cleans every document in the workspace.")] string? path = null,
        [Description("Return the diff without writing. Default false.")] bool dryRun = false,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, path, loaded => FormatService.CleanupAsync(
            loaded, path, new EditOptions("cleanup", dryRun, AllowErrors: false), cancellationToken));

    [McpServerTool(Name = "find_dead_code")]
    [Description("Unreferenced private and internal members, unused parameters and unreachable code across the workspace. Report only - delete with delete_symbol.")]
    public Task<string> FindDeadCode(
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        [Description("Maximum results, default 200.")] int maxResults = 0,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, null, loaded =>
            DeadCodeService.FindAsync(loaded, NavigationTools.Cap(maxResults, 200), cancellationToken));

    private static string[] Split(string? ids) =>
        string.IsNullOrWhiteSpace(ids) ? [] : [.. ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static DiagnosticSeverity Severity(string? minSeverity) => minSeverity?.ToLowerInvariant() switch
    {
        "error" => DiagnosticSeverity.Error,
        "warning" => DiagnosticSeverity.Warning,
        "hidden" => DiagnosticSeverity.Hidden,
        _ => DiagnosticSeverity.Info,
    };

    private Task<string> Guarded(string? workspace, string? path, Func<LoadedWorkspace, Task<Result<string>>> action)
    {
        var rejection = context.RejectWrite();

        return rejection is not null
            ? Task.FromResult(rejection)
            : context.WithWorkspaceAsync(workspace, path, async loaded =>
                NavigationTools.Unwrap(await action(loaded).ConfigureAwait(false)));
    }
}
