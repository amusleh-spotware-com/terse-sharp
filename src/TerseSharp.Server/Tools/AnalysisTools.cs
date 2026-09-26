using System.Buffers;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class AnalysisTools(ToolContext context, ReplayGate replay)
{
    [McpServerTool(Name = "analyze")]
    [Description("Compiler diagnostics, every analyzer the project references, and dead-code findings in one deduplicated list, down to info severity. Pass paths to analyze up to 10 files in ONE pass. Replaces one call per file, which is what the end-of-task sweep used to cost. Use instead of reading build output; catches unreferenced members, unused usings and style violations the build hides. Dead code is reported as TERSE001 in category DeadCode. Findings sharing an id, a severity and a message are folded onto one line carrying every position, and an id passed to ids= that no referenced analyzer declares is named NOT_ENABLED instead of answering a silent zero. baseRef=HEAD reports only the findings on lines this working tree ADDED or CHANGED against that ref - an untracked file counts whole - and folds the rest to one count, which is what makes the end-of-task verdict mean this task rather than this repository; a baseRef call always runs, because git state can move with nothing written. changed=true with no baseRef= is baseRef=HEAD on a git tree, so the end-of-task sweep reports what this task changed; baseRef=\"\" reports every finding in the changed files. A paths= batch that saturates the 10-path cap ends with next: analyze changed=true, which answers the same end-of-task sweep over every modified file in ONE call.")]
    public Task<string> Analyze(
            [Description("Scope to a file, a directory or a glob such as src/**/*.cs. Empty analyzes the whole solution.")] string? path = null,
            [Description("Minimum severity: error, warning, info, hidden. Default info.")] string? minSeverity = null,
            [Description("Alias for minSeverity.")] string? severity = null,
            [Description("Optional comma-separated diagnostic ids to keep, e.g. CA1822,TERSE001; a JSON-array spelling such as [\"CA1822\"] reads the same. An id no referenced analyzer declares is reported NOT_ENABLED.")] string? ids = null,
            [Description("Include unreferenced members and unreachable code. Default true; set false on a huge solution to skip the reference scan.")] bool includeDeadCode = true,
            [Description("Workspace or worktree name.")] string? workspace = null,
            [Description("Max results (200).")] int maxResults = 0,
            [Description("Report only diagnostics that appeared since the previous analyze of the same scope, and which ones were fixed.")] bool sinceLast = false,
            [Description("Limit the pass to files modified since the workspace loaded, so the end-of-task gate is one call.")] bool changed = false,
            [Description("Several files, directories or globs analyzed in one pass, at most 10. Combines with path, taken first; an entry carrying a comma or a brace is refused by name.")] string?[]? paths = null,
            [Description("Report only findings on a line the working tree added or changed against this git ref, e.g. HEAD or main, folding the rest to one pre-existing count. A file git does not track counts whole. Omitted beside changed=true it is HEAD on a git tree; an empty string reports every finding in scope.")] string? baseRef = null,
        CancellationToken cancellationToken = default) => replay.ReplayedAsync(
        "analyze",
        ReplayGate.Key("analyze", path, minSeverity, severity, ids, includeDeadCode.ToString(), maxResults.ToString(CultureInfo.InvariantCulture), changed.ToString(), paths is null ? null : string.Join(',', paths), baseRef, Defaulted(baseRef), workspace),
        sinceLast || baseRef is { Length: > 0 } || (changed && baseRef is null),
        () => context.WithWorkspaceAsync(
        workspace,
        path ?? First(paths),
        async loaded =>
            {
                var scope = Scoped(loaded, path, paths);

                if (!scope.IsOk)
                    return scope.Error!.Render();

                var (Touched, Error) = await ScopedTouchedAsync(loaded.Root, baseRef, changed, cancellationToken).ConfigureAwait(false);

                if (Error is { } failure)
                    return failure.Render();

                return await GateSteered(
                    Steered(
                        AnalysisService.AnalyzeAsync(
                            loaded, scope.Value, Severity(minSeverity ?? severity), Split(ids), includeDeadCode, NavigationTools.Cap(maxResults, 200), sinceLast, changed, Touched, Narrowing(baseRef, Touched), cancellationToken),
                        path,
                        paths,
                        changed),
                    path,
                    paths,
                    changed || sinceLast || ids is { Length: > 0 },
                check: false).ConfigureAwait(false);
            },
        cancellationToken: cancellationToken),
        cancellationToken);

    [McpServerTool(Name = "format")]
    [Description("Replaces Bash dotnet format whitespace. Reformats C# to the project's .editorconfig using the Roslyn formatter. path takes a file, a directory or a glob, paths=[...] takes up to 10 of them in ONE pass exactly as analyze does - Replaces one call per file - and changed=true limits the pass to files modified since the workspace loaded; verify=true returns a one-line verdict, replacing dotnet format --verify-no-changes. Reports one line per changed file; pass verbose=true for the diff.")]
    public Task<string> Format(
        [Description("File, directory or glob such as src/**/*.cs; empty formats every document.")] string? path = null,
        [Description("Only files modified since the workspace loaded. Use after an edit sweep to avoid drive-by changes.")] bool changed = false,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Report clean or VERIFY_FAILED with the files the Roslyn whitespace formatter would change, and write nothing. This is not the CI gate: dotnet format style and analyzers do not run the whitespace formatter, so a VERIFY_FAILED here can still be a green CI leg. Use cleanup verify=true fix=style and fix=analyzers to pre-empt CI.")] bool verify = false,
        [Description("Return the full diff instead of one line per changed file.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Several files, directories or globs formatted in one pass, at most 10. Combines with path, taken first; an entry carrying a comma or a brace is refused by name.")] string?[]? paths = null,
        CancellationToken cancellationToken = default) =>
        GateSteered(
            Guarded(workspace, path ?? First(paths), loaded =>
            {
                var scope = Scoped(loaded, path, paths);

                return scope.IsOk
                    ? FormatService.RunAsync(
                        loaded,
                        new FixScope(scope.Value, changed),
                        new FixRequest(FixMode.None, [], DiagnosticSeverity.Info, verify),
                        new EditOptions("format", dryRun, AllowErrors: false, Verbose: verbose, AllowPolicy: true),
                        cancellationToken)
                    : Task.FromResult(Result.Fail<string>(scope.Error!));
            }),
            path,
            paths,
            changed,
            verify || dryRun);

    [McpServerTool(Name = "cleanup")]
    [Description("Replaces Bash dotnet format style and dotnet format analyzers. fix=ci applies BOTH CI rule sets in ONE pass and answers one verdict; fix=usings and fix=all remove unused usings, sort them System-first and reformat; fix=style and fix=analyzers apply code fixes ONLY and never reformat, so each matches its CI command byte for byte. Those fix modes apply the code fixes of every analyzer the project references, reporting UNFIXED for a diagnostic no fixer covers. path takes a file, a directory or a glob, paths=[...] up to 10 in ONE pass as analyze and format do - Replaces one call per file - and changed=true limits the pass to files modified since the workspace loaded. Reports one line per changed file (verbose=true for the diff) and is rolled back if it breaks the build.")]
    public Task<string> Cleanup(
        [Description("File, directory or glob such as src/**/*.cs; empty cleans every document.")] string? path = null,
        [Description("usings (default), style for IDE code fixes, analyzers for CA and third-party code fixes, ci for both CI rule sets in one pass, or all.")] string? fix = null,
        [Description("Optional comma-separated diagnostic ids to fix, e.g. IDE0005,CA1822.")] string? ids = null,
        [Description("Minimum severity to fix: error, warning, info, hidden. Default info.")] string? severity = null,
        [Description("Only files modified since the workspace loaded. Use after an edit sweep to avoid drive-by changes.")] bool changed = false,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Report clean or VERIFY_FAILED with the files that would change, and write nothing. fix=ci verifies BOTH CI commands in one call, tagging each named file style, analyzers or style+analyzers; fix=style and fix=analyzers verify one each; fix=all and the default fix=usings are supersets and can name files CI accepts.")] bool verify = false,
        [Description("Return the full diff instead of one line per changed file.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Several files, directories or globs in one pass, at most 10. Combines with path, taken first; an entry carrying a comma or a brace is refused by name.")] string?[]? paths = null,
        CancellationToken cancellationToken = default)
    {
        var mode = Mode(fix);

        if (!mode.IsOk)
            return Task.FromResult(mode.Error!.Render());

        return GateSteered(
            Guarded(workspace, path ?? First(paths), loaded =>
            {
                var scope = Scoped(loaded, path, paths);

                return scope.IsOk
                    ? FormatService.RunAsync(
                        loaded,
                        new FixScope(scope.Value, changed),
                        new FixRequest(mode.Value, Split(ids), Severity(severity), verify) { MirrorsCi = verify },
                        new EditOptions("cleanup", dryRun, AllowErrors: false, Verbose: verbose, AllowPolicy: true),
                        cancellationToken)
                    : Task.FromResult(Result.Fail<string>(scope.Error!));
            }),
            path,
            paths,
            changed,
            verify || dryRun);
    }

    private static Result<FixMode> Mode(string? fix) => fix?.ToLowerInvariant() switch
    {
        null or "" or "usings" => Result.Ok(FixMode.Usings),
        "style" => Result.Ok(FixMode.Style),
        "analyzers" => Result.Ok(FixMode.Analyzers),
        "all" => Result.Ok(FixMode.All),
        "ci" => Result.Ok(FixMode.Ci),
        _ => Result.Fail<FixMode>(Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"fix='{fix}' is not a known mode"),
            "pass fix=ci for both CI rule sets in one pass, or fix=usings, style, analyzers or all")),
    };

    private const string IdPunctuation = "[]\"' \t\r\n";

    private static string[] Split(string? ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
            return [];

        var text = ids.AsSpan();
        var kept = new List<string>(text.Count(',') + 1);

        foreach (var range in text.Split(','))
        {
            var id = text[range].Trim(IdPunctuation);

            if (!id.IsEmpty)
                kept.Add(id.ToString());
        }

        return [.. kept];
    }

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
    [Description("Run the end-of-task quality gate in the order this project mandates - analyze at info severity, format, cleanup fix=all, analyze again - over the files changed since the workspace loaded, and answer one verdict line instead of four calls. A clean run is 'clean  analyzed=N fixed=M remaining=0', where analyzed is how many documents were in scope, so a clean verdict can never be mistaken for a gate that ran over nothing; anything else keeps the diagnostics that are still unfixed. A scope matching no document answers an error naming it, never a verdict. paths= gates several files, directories or globs as ONE verdict. Replaces one call per scope. baseRef=HEAD keeps only the findings on lines this working tree changed against that ref and writes only the files it changed, which makes the verdict mean this task rather than this repository; over the changed-file default it is baseRef=HEAD on a git tree unless baseRef=\"\" is passed, and a narrowed verdict ends preExisting=N counting what it folded. dryRun=true verifies instead of writing, solution=true gates every document, and verbose=true adds each step's own report.")]
    public Task<string> Gate(
            [Description("Scope to a file, a directory or a glob such as src/**/*.cs. Empty gates the files modified since the workspace loaded.")] string? path = null,
            [Description("Gate every document instead of only the files modified since the workspace loaded. Ignored when path is passed. Default false.")] bool solution = false,
            [Description("Verify instead of writing: format and cleanup report what they would change and nothing is modified. Default false.")] bool dryRun = false,
            [Description("Add each step's own report under the verdict line. Default false.")] bool verbose = false,
            [Description("Workspace or worktree name.")] string? workspace = null,
            [Description("Documents gate's changed-file default. Ignored beside path= or solution=; false is refused otherwise.")] bool? changed = null,
            [Description("Report only findings on a line the working tree added or changed against this git ref, e.g. HEAD. A file git does not track counts whole. Omitted over the changed-file default it is HEAD on a git tree; an empty string reports every finding in scope.")] string? baseRef = null,
            [Description("Several scopes gated as ONE verdict, each a file, a directory or a glob, at most 10. Combines with path, which is taken first; an entry matching no document is refused by name.")] string?[]? paths = null,
            CancellationToken cancellationToken = default) =>
            RejectedChanged(changed, path ?? FirstScope(paths), solution) is { } rejected
                ? Task.FromResult(rejected)
                : replay.ReplayedAsync(
                "gate",
                ReplayGate.Key("gate", path, solution.ToString(), dryRun.ToString(), verbose.ToString(), baseRef, Defaulted(baseRef), workspace, paths is null ? null : string.Join('\n', paths)),
                !dryRun || baseRef is { Length: > 0 } || (baseRef is null && path is null && paths is null && !solution),
                () => Guarded(workspace, path ?? FirstScope(paths), async Task<Result<string>> (loaded) =>
                {
                    var (Scope, Refusal) = GateScope(loaded, path, paths);

                    if (Refusal is { } refused)
                        return Result.Fail<string>(refused);

                    var changedScope = Scope is null && !solution;
                    var (Touched, Error) = await ScopedTouchedAsync(loaded.Root, baseRef, changedScope, cancellationToken).ConfigureAwait(false);

                    return Error is { } failure
                        ? Result.Fail<string>(failure)
                        : await GateService.RunAsync(
                            loaded,
                        new GateRequest(Scope, Changed: changedScope, dryRun, verbose, Touched),
                        cancellationToken).ConfigureAwait(false);
                }),
                cancellationToken);

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

    private static bool Saturated(string? path, string?[]? paths)
    {
        var distinct = new HashSet<string>(PluralPaths.MaxPaths + 1, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(path))
            distinct.Add(path);

        foreach (var entry in paths ?? [])
        {
            if (!string.IsNullOrWhiteSpace(entry))
                distinct.Add(entry);
        }

        return distinct.Count >= PluralPaths.MaxPaths;
    }

    private static async Task<string> Steered(Task<string> answer, string? path, string?[]? paths, bool changed)
    {
        var text = await answer.ConfigureAwait(false);

        return changed || !Saturated(path, paths)
            ? text
            : text + "\nnext: analyze changed=true - ONE pass over every file modified since the workspace loaded, instead of a second paths= batch";
    }

    private static string? RejectedChanged(bool? changed, string? path, bool solution) => changed is false && path is null && !solution
            ? Errors.Invalid(
                "gate is always scoped to the files modified since the workspace loaded, so changed=false has no meaning",
                "drop changed=, or pass solution=true to gate every document instead").Render()
            : null;

    private static bool Unscoped(string? path, string?[]? paths, bool changed) =>
        !changed && path is not { Length: > 0 } && First(paths) is null;

    private static async Task<string> GateSteered(Task<string> answer, string? path, string?[]? paths, bool changed, bool check)
    {
        var text = await answer.ConfigureAwait(false);

        if (!Unscoped(path, paths, changed) || text.StartsWith("ERROR", StringComparison.Ordinal))
            return text;

        return text + (check
            ? "\nnext: gate dryRun=true - the unscoped end-of-task sweep (analyze at info, format, cleanup fix=all, re-analyze), verified in ONE call"
            : "\nnext: gate - the unscoped end-of-task sweep (analyze at info, format, cleanup fix=all, re-analyze) in ONE call");
    }

    internal static async Task<(TouchedLines? Touched, TerseError? Error)> TouchedAsync(string root, string? baseRef, CancellationToken cancellationToken)
    {
        if (baseRef is not { Length: > 0 } reference)
            return (null, null);

        var diff = await GitRunner.ReadAsync(root, ["diff", "--unified=0", "--no-color", "--relative", reference], cancellationToken).ConfigureAwait(false);

        if (!diff.IsOk)
            return (null, diff.Error!);

        var untracked = await GitRunner.ReadAsync(root, ["--no-optional-locks", "ls-files", "--others", "--exclude-standard"], cancellationToken).ConfigureAwait(false);

        return untracked.IsOk
            ? (TouchedLines.From(diff.Value!, untracked.Value!.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), root), null)
            : (null, untracked.Error!);
    }

    private const string DefaultBaseRef = "HEAD";

    private static Task<(TouchedLines? Touched, TerseError? Error)> ScopedTouchedAsync(string root, string? baseRef, bool changedScope, CancellationToken cancellationToken) =>
        baseRef is null && changedScope
            ? DefaultTouchedAsync(root, cancellationToken)
            : TouchedAsync(root, baseRef, cancellationToken);

    private static async Task<(TouchedLines? Touched, TerseError? Error)> DefaultTouchedAsync(string root, CancellationToken cancellationToken)
    {
        var (touched, _) = await TouchedAsync(root, DefaultBaseRef, cancellationToken).ConfigureAwait(false);

        return (touched, null);
    }

    private static string Defaulted(string? baseRef) => baseRef is null ? "defaulted" : "explicit";


    private static string Narrowing(string? baseRef, TouchedLines? touched) =>
        baseRef ?? (touched is null ? string.Empty : DefaultBaseRef);


    private static string? FirstScope(string?[]? paths) => paths is [{ Length: > 0 } first, ..] ? first : null;

    private static (string? Scope, TerseError? Refusal) GateScope(LoadedWorkspace loaded, string? path, string?[]? paths)
    {
        if (paths is null)
            return (path, null);

        var combined = PluralPaths.Combine(path, paths, "paths");

        if (!combined.IsOk)
            return (null, combined.Error);

        return Unmatched(loaded, combined.Value) is { } refusal
            ? (null, refusal)
            : (Union(loaded.Root, combined.Value), null);
    }

    private static TerseError? Unmatched(LoadedWorkspace loaded, ImmutableArray<string> scopes)
    {
        foreach (var scope in scopes)
        {
            if (scopes.Length > 1 && CommaOutsideBraces(scope))
                return Errors.Invalid("paths entry '" + scope + "' carries a comma, which cannot join a paths= union", "gate that entry alone with path=");

            if (DocumentScope.Select(loaded, scope, changedOnly: false).Length is 0)
                return Errors.Invalid("paths entry '" + scope + "' matches no document", "fix or drop that entry - every paths= scope must name at least one document");
        }

        return null;
    }

    private static bool CommaOutsideBraces(ReadOnlySpan<char> scope)
    {
        var depth = 0;

        foreach (var character in scope)
        {
            depth += character switch { '{' => 1, '}' => -1, _ => 0 };

            if (character is ',' && depth is 0)
                return true;
        }

        return false;
    }

    private static string Union(string root, ImmutableArray<string> scopes) => scopes is [var only]
        ? only
        : "{" + string.Join(',', scopes.Select(scope => Alternatives(root, scope))) + "}";

    private static string Alternatives(string root, string scope)
    {
        var relative = (Path.IsPathRooted(scope) ? Path.GetRelativePath(root, scope) : scope).Replace('\\', '/').TrimEnd('/');

        return scope.AsSpan().IndexOfAny(GlobCharacters) >= 0 ? relative : relative + "," + relative + "/**";
    }
}
