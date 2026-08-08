using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class AnalysisTools(ToolContext context)
{
    [McpServerTool(Name = "analyze")]
    [Description("Compiler diagnostics, every analyzer the project references, and dead-code findings in one deduplicated list, down to info severity. Use instead of reading build output; catches unreferenced members, unused usings and style violations the build hides. Dead code is reported as TERSE001 in category DeadCode.")]
    public Task<string> Analyze(
        [Description("Scope to a file, a directory or a glob such as src/**/*.cs. Empty analyzes the whole solution.")] string? path = null,
        [Description("Minimum severity: error, warning, info, hidden. Default info.")] string? minSeverity = null,
        [Description("Optional comma-separated diagnostic ids to keep, e.g. CA1822,TERSE001.")] string? ids = null,
        [Description("Include unreferenced members and unreachable code. Default true; set false on a huge solution to skip the reference scan.")] bool includeDeadCode = true,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (200).")] int maxResults = 0,
        [Description("Report only diagnostics that appeared since the previous analyze of the same scope, and which ones were fixed.")] bool sinceLast = false,
        [Description("Limit the pass to files modified since the workspace loaded, so the end-of-task gate is one call.")] bool changed = false,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, path, loaded => AnalysisService.AnalyzeAsync(
            loaded, path, Severity(minSeverity), Split(ids), includeDeadCode, NavigationTools.Cap(maxResults, 200), sinceLast, changed, cancellationToken),
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "format")]
    [Description("Replaces Bash dotnet format whitespace. Reformats C# to the project's .editorconfig using the Roslyn formatter. path takes a file, a directory or a glob, and changed=true limits the pass to files modified since the workspace loaded; verify=true returns a one-line verdict, replacing dotnet format --verify-no-changes. Reports one line per changed file; pass verbose=true for the diff.")]
    public Task<string> Format(
        [Description("File, directory or glob such as src/**/*.cs; empty formats every document.")] string? path = null,
        [Description("Only files modified since the workspace loaded. Use after an edit sweep to avoid drive-by changes.")] bool changed = false,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Report clean or VERIFY_FAILED with the files the Roslyn whitespace formatter would change, and write nothing. This is not the CI gate: dotnet format style and analyzers do not run the whitespace formatter, so a VERIFY_FAILED here can still be a green CI leg. Use cleanup verify=true fix=style and fix=analyzers to pre-empt CI.")] bool verify = false,
        [Description("Return the full diff instead of one line per changed file.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, path, loaded => FormatService.RunAsync(
            loaded,
            new FixScope(path, changed),
            new FixRequest(FixMode.None, [], DiagnosticSeverity.Info, verify),
            new EditOptions("format", dryRun, AllowErrors: false, Verbose: verbose),
            cancellationToken));

    [McpServerTool(Name = "cleanup")]
    [Description("Replaces Bash dotnet format style and dotnet format analyzers. Removes unused using directives, sorts the remaining ones System-first, then reformats; fix=style, analyzers or all also applies the code fixes of every analyzer the project references, reporting UNFIXED for a diagnostic no fixer covers. path takes a file, a directory or a glob, and changed=true limits the pass to files modified since the workspace loaded. Reports one line per changed file (verbose=true for the diff) and is rolled back if it breaks the build.")]
    public Task<string> Cleanup(
        [Description("File, directory or glob such as src/**/*.cs; empty cleans every document.")] string? path = null,
        [Description("usings (default), style for IDE code fixes, analyzers for CA and third-party code fixes, or all.")] string? fix = null,
        [Description("Optional comma-separated diagnostic ids to fix, e.g. IDE0005,CA1822.")] string? ids = null,
        [Description("Minimum severity to fix: error, warning, info, hidden. Default info.")] string? severity = null,
        [Description("Only files modified since the workspace loaded. Use after an edit sweep to avoid drive-by changes.")] bool changed = false,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Report clean or VERIFY_FAILED with the files that would change, and write nothing. fix=style verifies exactly what dotnet format style checks and fix=analyzers exactly what dotnet format analyzers checks, so those two are the CI pre-empt; fix=all and the default fix=usings are supersets and can name files CI accepts.")] bool verify = false,
        [Description("Return the full diff instead of one line per changed file.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default)
    {
        var mode = Mode(fix);

        return mode.IsOk
            ? Guarded(workspace, path, loaded => FormatService.RunAsync(
                loaded,
                new FixScope(path, changed),
                new FixRequest(mode.Value, Split(ids), Severity(severity), verify),
                new EditOptions("cleanup", dryRun, AllowErrors: false, Verbose: verbose),
                cancellationToken))
            : Task.FromResult(mode.Error!.Render());
    }

    private static Result<FixMode> Mode(string? fix) => fix?.ToLowerInvariant() switch
    {
        null or "" or "usings" => Result.Ok(FixMode.Usings),
        "style" => Result.Ok(FixMode.Style),
        "analyzers" => Result.Ok(FixMode.Analyzers),
        "all" => Result.Ok(FixMode.All),
        _ => Result.Fail<FixMode>(Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"fix='{fix}' is not a known mode"),
            "pass fix=usings, style, analyzers or all")),
    };

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

    [McpServerTool(Name = "gate")]
    [Description("Run the end-of-task quality gate in the order this project mandates - analyze at info severity, format, cleanup fix=all, analyze again - over the files changed since the workspace loaded, and answer one verdict line instead of four calls. A clean run is 'clean  analyzed=N fixed=M remaining=0'; anything else keeps the diagnostics that are still unfixed. dryRun=true verifies instead of writing, solution=true gates every document, and verbose=true adds each step's own report.")]
    public Task<string> Gate(
        [Description("Scope to a file, a directory or a glob such as src/**/*.cs. Empty gates the files modified since the workspace loaded.")] string? path = null,
        [Description("Gate every document instead of only the files modified since the workspace loaded. Ignored when path is passed. Default false.")] bool solution = false,
        [Description("Verify instead of writing: format and cleanup report what they would change and nothing is modified. Default false.")] bool dryRun = false,
        [Description("Add each step's own report under the verdict line. Default false.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, path, loaded => GateService.RunAsync(
            loaded,
            new GateRequest(path, Changed: path is null && !solution, dryRun, verbose),
            cancellationToken));
}
