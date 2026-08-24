using System.Buffers;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class AnalysisTools(ToolContext context)
{
    [McpServerTool(Name = "analyze")]
    [Description("Compiler diagnostics, every analyzer the project references, and dead-code findings in one deduplicated list, down to info severity. Pass paths to analyze up to 10 files in ONE pass. Replaces one call per file, which is what the end-of-task sweep used to cost. Use instead of reading build output; catches unreferenced members, unused usings and style violations the build hides. Dead code is reported as TERSE001 in category DeadCode. Findings sharing an id, a severity and a message are folded onto one line carrying every position, and an id passed to ids= that no referenced analyzer declares is named NOT_ENABLED instead of answering a silent zero.")]
    public Task<string> Analyze(
            [Description("Scope to a file, a directory or a glob such as src/**/*.cs. Empty analyzes the whole solution.")] string? path = null,
            [Description("Minimum severity: error, warning, info, hidden. Default info.")] string? minSeverity = null,
            [Description("Alias for minSeverity.")] string? severity = null,
            [Description("Optional comma-separated diagnostic ids to keep, e.g. CA1822,TERSE001. An id no referenced analyzer declares is reported NOT_ENABLED.")] string? ids = null,
            [Description("Include unreferenced members and unreachable code. Default true; set false on a huge solution to skip the reference scan.")] bool includeDeadCode = true,
            [Description("Workspace or worktree name.")] string? workspace = null,
            [Description("Max results (200).")] int maxResults = 0,
            [Description("Report only diagnostics that appeared since the previous analyze of the same scope, and which ones were fixed.")] bool sinceLast = false,
            [Description("Limit the pass to files modified since the workspace loaded, so the end-of-task gate is one call.")] bool changed = false,
            [Description("Several files, directories or globs analyzed in one pass, at most 10. Combines with path, which is taken first; an entry carrying a comma or a brace is refused by name rather than mis-scoped.")] string?[]? paths = null,
            CancellationToken cancellationToken = default) => context.WithWorkspaceAsync(
            workspace,
            path ?? First(paths),
            loaded =>
            {
                var scope = Scoped(loaded, path, paths);

                return scope.IsOk
                    ? AnalysisService.AnalyzeAsync(
                        loaded, scope.Value, Severity(minSeverity ?? severity), Split(ids), includeDeadCode, NavigationTools.Cap(maxResults, 200), sinceLast, changed, cancellationToken)
                    : Task.FromResult(scope.Error!.Render());
            },
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
    [Description("Replaces Bash dotnet format style and dotnet format analyzers. fix=usings and fix=all remove unused usings, sort them System-first and reformat; fix=style and fix=analyzers apply code fixes ONLY and never reformat, so each matches its CI command byte for byte. Those three fix modes apply the code fixes of every analyzer the project references, reporting UNFIXED for a diagnostic no fixer covers. path takes a file, a directory or a glob, and changed=true limits the pass to files modified since the workspace loaded. Reports one line per changed file (verbose=true for the diff) and is rolled back if it breaks the build.")]
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
    [Description("Run the end-of-task quality gate in the order this project mandates - analyze at info severity, format, cleanup fix=all, analyze again - over the files changed since the workspace loaded, and answer one verdict line instead of four calls. A clean run is 'clean  analyzed=N fixed=M remaining=0', where analyzed is how many documents were in scope, so a clean verdict can never be mistaken for a gate that ran over nothing; anything else keeps the diagnostics that are still unfixed. A scope matching no document answers an error naming it, never a verdict. dryRun=true verifies instead of writing, solution=true gates every document, and verbose=true adds each step's own report.")]
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

    private static string? First(string?[]? paths) =>
            paths is { Length: > 0 } ? Array.Find(paths, entry => entry is { Length: > 0 }) : null;

    private static Result<string?> Scoped(LoadedWorkspace loaded, string? path, string?[]? paths)
    {
        if (paths is not { Length: > 0 })
            return Result.Ok(path);

        if (paths.Length > MaxScopedPaths)
        {
            return Result.Fail<string?>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'paths' carried {paths.Length} entries, at most {MaxScopedPaths} are analyzed in one call"),
                string.Create(CultureInfo.InvariantCulture, $"send at most {MaxScopedPaths} per call")));
        }

        var entries = new List<string>(paths.Length + 1);

        if (path is { Length: > 0 })
            entries.Add(Relative(loaded.Root, path));

        foreach (var entry in paths)
        {
            if (Refusable(entry) is { } refusal)
                return Result.Fail<string?>(refusal);

            entries.Add(Relative(loaded.Root, entry!));
        }

        if (entries.Count is 1)
            return Result.Ok<string?>(entries[0]);

        for (var index = 0; index < entries.Count; index++)
            entries[index] = Widened(loaded.Root, entries[index]);

        return Result.Ok<string?>("{" + string.Join(',', entries) + "}");
    }

    private static TerseError? Refusable(string? entry) => entry switch
    {
        not { Length: > 0 } => Errors.Invalid(
            "'paths' carries a blank entry",
            "pass a file, a directory or a glob per entry"),
        _ when entry.Contains(',', StringComparison.Ordinal) || entry.AsSpan().IndexOfAny('{', '}') >= 0 => Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"'paths' entry '{entry}' carries a comma or a brace, which is how several scopes are combined"),
            "send that entry as its own call, or pass it as path="),
        _ => null,
    };

    private static string Relative(string root, string entry) => Path.IsPathRooted(entry)
            ? Path.GetRelativePath(root, Path.GetFullPath(entry)).Replace('\\', '/')
            : entry;

    private const int MaxScopedPaths = 10;

    private static string Widened(string root, string entry)
    {
        if (entry.AsSpan().IndexOfAny(GlobCharacters) >= 0)
            return entry;

        var full = Path.IsPathRooted(entry) ? entry : Path.Combine(root, entry);

        return Directory.Exists(full) ? entry.TrimEnd('/', '\\') + "/**/*" : entry;
    }

    private static readonly SearchValues<char> GlobCharacters = SearchValues.Create("*?{");
}
