using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class AnalysisTools(ToolContext context)
{
    [McpServerTool(Name = "analyze")]
    [Description("Compiler diagnostics, every analyzer the project references, and dead-code findings in one deduplicated list, down to info severity. Use instead of reading build output; catches unreferenced members, unused usings and style violations the build hides. Dead code is reported as TERSE001 in category DeadCode.")]
    public Task<string> Analyze(
        [Description("File to scope to.")] string? path = null,
        [Description("Minimum severity: error, warning, info, hidden. Default info.")] string? minSeverity = null,
        [Description("Optional comma-separated diagnostic ids to keep, e.g. CA1822,TERSE001.")] string? ids = null,
        [Description("Include unreferenced members and unreachable code. Default true; set false on a huge solution to skip the reference scan.")] bool includeDeadCode = true,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (200).")] int maxResults = 0,
        [Description("Report only diagnostics that appeared since the previous analyze of the same scope, and which ones were fixed.")] bool sinceLast = false,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, path, loaded => AnalysisService.AnalyzeAsync(
            loaded, path, Severity(minSeverity), Split(ids), includeDeadCode, NavigationTools.Cap(maxResults, 200), sinceLast, cancellationToken));

    [McpServerTool(Name = "format")]
    [Description("Reformat C# to the project's .editorconfig using the Roslyn formatter. Returns the diff, never the file.")]
    public Task<string> Format(
        [Description("File path; empty formats every document.")] string? path = null,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, path, loaded => FormatService.FormatAsync(
            loaded, path, new EditOptions("format", dryRun, AllowErrors: false), cancellationToken));

    [McpServerTool(Name = "cleanup")]
    [Description("Remove unused using directives, sort the remaining ones System-first, then reformat. Returns the diff and is rolled back if it breaks the build.")]
    public Task<string> Cleanup(
        [Description("File path; empty cleans every document.")] string? path = null,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, path, loaded => FormatService.CleanupAsync(
            loaded, path, new EditOptions("cleanup", dryRun, AllowErrors: false), cancellationToken));

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
