using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

public static class EditGate
{
    public static async Task<Result<string>> ApplyAsync(
        LoadedWorkspace workspace,
        Solution updated,
        IReadOnlyList<DocumentId> changed,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var adopted = await AdoptEndingsAsync(workspace, updated, changed, cancellationToken).ConfigureAwait(false);
        var diff = await DiffAsync(workspace.Solution, adopted, changed, cancellationToken).ConfigureAwait(false);

        var report = options.AllowErrors
            ? null
            : await AnalyseAsync(workspace.Solution, adopted, changed, workspace.Root, options.Usings, cancellationToken).ConfigureAwait(false);

        var policy = await PolicyGate.EvaluateAsync(workspace, adopted, changed, options.AllowPolicy, cancellationToken).ConfigureAwait(false);

        if (options.DryRun)
            return Result.Ok(Render(options, diff, "dryRun", report, workspace.Root, policy));

        if (Blocked(report, policy, options.Tool) is { } failure)
            return Result.Fail<string>(failure);

        if (!await workspace.TryApplyAsync(adopted, changed, cancellationToken).ConfigureAwait(false))
            return Result.Fail<string>(Errors.EditConflict("the workspace rejected the change"));

        EditPulse.Bump(diff.Length);

        return Result.Ok(Render(options, diff, "applied", report, workspace.Root, policy));
    }

    private static string Render(EditOptions options, DocumentDiff[] diffs, string state, GateReport? report, string root, PolicyVerdict policy)
    {
        var response = new ResponseBuilder(options.Tool, state).Verbose(options.Verbose);
        var condensed = Condensed(options, diffs, report, policy);

        if (!condensed)
            response.Summary(diffs.Length, diffs.Length, "files changed");

        if (diffs.Length is 0)
            response.Note("no change - the result is identical to what is already there");

        if (options.DryRun && !options.Verbose)
            response.Note("dryRun");

        if (report is not null)
            Announce(response, report, options.Verbose || options.DryRun, options.Tool);

        Policy(response, policy, options.DryRun);

        if (!options.DryRun && report is { NewErrors.Length: 0 } && GateCoverage.Once() is { } coverage)
            response.Note(coverage);

        if (condensed)
            return Compact(response, diffs, root);

        foreach (var diff in diffs)
            response.Line(diff.Text).Line(string.Create(CultureInfo.InvariantCulture, $"changedLines={diff.ChangedLines}"));

        return response.ToString();
    }
    private const int MaxNewWarnings = 5;

    private static void Announce(ResponseBuilder response, GateReport report, bool verbose, string tool)
    {
        if (Describe(report, verbose) is { Length: > 0 } counters)
            response.Note(counters);

        foreach (var warning in report.NewWarnings.Take(MaxNewWarnings))
            response.Note("WARNING introduced  " + warning);

        if (report.NewWarnings.Length > MaxNewWarnings)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"WARNING introduced {MaxNewWarnings} of {report.NewWarnings.Length} shown - analyze the changed files for the rest"));

        if (report.Unresolved.Length > 0)
            Unresolved(response, report);

        if (report.NewErrors.Length > 0)
            Rejected(response, report, tool);
    }

    private static void Unresolved(ResponseBuilder response, GateReport report)
    {
        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"UNRESOLVED {report.Unresolved.Length} name(s) this project does not resolve; the edit was applied, not rolled back"));

        foreach (var unresolved in report.Unresolved)
            response.Note(unresolved);

        response.Note("remedy: add the missing reference to the project, or accept it if the name is resolved by a build the workspace has not seen");
    }

    private static void Rejected(ResponseBuilder response, GateReport report, string tool)
    {
        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"WARNING this edit introduces {report.NewErrors.Length} new error(s) and would be rolled back"));

        foreach (var error in report.NewErrors)
            response.Note(error);

        if (report.Collisions is { Length: > 0 } collisions)
            response.Note(Errors.Ambiguity(collisions, tool));

        if (report.Imports is { Length: > 0 } imports)
            response.Note(Errors.Missing(imports, tool));

        if (report.Callers is { Length: > 0 } callers)
            response.Note(Errors.CallerBatch(callers));
    }

    private static string Describe(GateReport report, bool verbose) => verbose
        ? ResponseCompression.VerboseCounters(report.Errors, report.ErrorDelta, report.Warnings, report.WarningDelta)
        : ResponseCompression.Counters(report.Errors, report.ErrorDelta, report.Warnings, report.WarningDelta);

    private static async Task<DocumentDiff[]> DiffAsync(
        Solution before,
        Solution after,
        IReadOnlyList<DocumentId> changed,
        CancellationToken cancellationToken)
    {
        var diffs = new List<DocumentDiff>(changed.Count);

        foreach (var id in changed)
        {
            var diff = await DocumentDiff.CreateAsync(before, after, id, cancellationToken).ConfigureAwait(false);

            if (diff is not null)
                diffs.Add(diff);
        }

        return [.. diffs];
    }

    private static async Task<GateReport> AnalyseAsync(
        Solution before,
        Solution after,
        IReadOnlyList<DocumentId> changed,
        string root,
        ImmutableArray<string> usings,
        CancellationToken cancellationToken)
    {
        var projects = Affected(before, changed);
        var baseline = await TallyAsync(before, projects, root, cancellationToken).ConfigureAwait(false);
        var current = await TallyAsync(after, projects, root, cancellationToken).ConfigureAwait(false);
        var appeared = current.Errors.Where(entry => Appeared(baseline.Errors, entry)).Select(entry => entry.Key).ToArray();
        var arrived = Arrived(before, after, changed, root);
        var regressions = appeared.Where(key => !Unresolvable(key, arrived, baseline.Errors)).Take(10).ToArray();

        return new GateReport(
            regressions,
            [.. appeared.Where(key => Unresolvable(key, arrived, baseline.Errors)).Take(10)],
            current.ErrorCount,
            current.ErrorCount - baseline.ErrorCount,
            current.WarningCount,
            current.WarningCount - baseline.WarningCount,
            await ImportHintAsync(after, changed, root, regressions, cancellationToken).ConfigureAwait(false),
            await CallerHintAsync(after, root, regressions, current.Lines, cancellationToken).ConfigureAwait(false),
            [.. current.Warnings.Where(entry => Appeared(baseline.Warnings, entry)).Select(entry => entry.Key).Order(StringComparer.Ordinal)],
            Collided(regressions, usings));
    }

    internal static bool Unresolvable(string key, HashSet<string> arrived, Dictionary<string, int> baseline) =>
        UnresolvedName(key) && (baseline.ContainsKey(key) || InArrivedFile(key, arrived));

    private static bool InArrivedFile(string key, HashSet<string> arrived)
    {
        if (arrived.Count is 0)
            return false;

        var start = key.IndexOf(' ', StringComparison.Ordinal) + 1;
        var end = key.IndexOf(": ", StringComparison.Ordinal);

        return start > 0 && end > start && arrived.Contains(key[start..end]);
    }

    private static HashSet<string> Arrived(Solution before, Solution after, IReadOnlyList<DocumentId> changed, string root)
    {
        var arrived = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in changed)
        {
            if (before.GetDocument(id) is null && after.GetDocument(id) is { FilePath: { } path })
                arrived.Add(PositionFormat.Relative(root, path));
        }

        return arrived;
    }

    private static bool UnresolvedName(string key) =>
        key.StartsWith("CS0246 ", StringComparison.Ordinal) || key.StartsWith("CS0234 ", StringComparison.Ordinal);

    private static ProjectId[] Affected(Solution solution, IReadOnlyList<DocumentId> changed)
    {
        var graph = solution.GetProjectDependencyGraph();

        return [.. changed
            .Select(id => id.ProjectId)
            .SelectMany(id => graph.GetProjectsThatTransitivelyDependOnThisProject(id).Append(id))
            .Distinct()];
    }

    private static bool Appeared(Dictionary<string, int> baseline, KeyValuePair<string, int> entry) =>
        entry.Value > baseline.GetValueOrDefault(entry.Key);

    private static async Task<Tally> TallyAsync(
            Solution solution,
            IReadOnlyList<ProjectId> projects,
            string root,
            CancellationToken cancellationToken)
    {
        var tally = new Tally(
            new Dictionary<string, int>(StringComparer.Ordinal),
            new Dictionary<string, int>(StringComparer.Ordinal),
            new Dictionary<string, List<int>>(StringComparer.Ordinal),
            0,
            0);

        foreach (var projectId in projects)
        {
            var compilation = await Compile(solution, projectId, cancellationToken).ConfigureAwait(false);

            if (compilation is not null)
                tally = Collect(tally, compilation.GetDiagnostics(cancellationToken), root);
        }

        return tally;
    }

    private static Tally Collect(Tally tally, IEnumerable<Diagnostic> diagnostics, string root)
    {
        var errors = tally.ErrorCount;
        var warnings = tally.WarningCount;

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity is DiagnosticSeverity.Error)
                errors += Record(tally.Errors, tally.Lines, diagnostic, root);

            if (diagnostic.Severity is DiagnosticSeverity.Warning)
                warnings += Record(tally.Warnings, tally.Lines, diagnostic, root);
        }

        return tally with { ErrorCount = errors, WarningCount = warnings };
    }

    private static int Record(Dictionary<string, int> errors, Dictionary<string, List<int>> lines, Diagnostic diagnostic, string root)
    {
        var span = diagnostic.Location.GetLineSpan();

        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{diagnostic.Id} {PositionFormat.Relative(root, span.Path)}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}");

        errors[key] = errors.GetValueOrDefault(key) + 1;
        Note(lines, key, span.StartLinePosition.Line + 1);

        return 1;
    }

    private static Task<Compilation?> Compile(Solution solution, ProjectId projectId, CancellationToken cancellationToken)
    {
        var project = solution.GetProject(projectId);

        return project is null ? Task.FromResult<Compilation?>(null) : project.GetCompilationAsync(cancellationToken);
    }

    private sealed record GateReport(
        string[] NewErrors,
        string[] Unresolved,
        int Errors,
        int ErrorDelta,
        int Warnings,
        int WarningDelta,
        string[]? Imports,
        string[]? Callers,
        string[] NewWarnings,
        string[]? Collisions);

    private readonly record struct Tally(
        Dictionary<string, int> Errors,
        Dictionary<string, int> Warnings,
        Dictionary<string, List<int>> Lines,
        int ErrorCount,
        int WarningCount);
    private static string Compact(ResponseBuilder response, DocumentDiff[] diffs, string root)
    {
        foreach (var diff in diffs)
        {
            response.Line(string.Create(
                CultureInfo.InvariantCulture,
                $"{PositionFormat.Relative(root, diff.Path)}  changedLines={diff.ChangedLines}"));
        }

        return response.ToString();
    }

    private static bool Condensed(EditOptions options, DocumentDiff[] diffs, GateReport? report, PolicyVerdict policy) =>
            !options.Verbose
            && !options.DryRun
            && diffs.Length is not 0
            && policy.Quiet
            && report is not { NewErrors.Length: > 0 }
            && report is not { Unresolved.Length: > 0 };

    private static async Task<string?> EndingAsync(
        LoadedWorkspace workspace,
        Solution before,
        DocumentId id,
        CancellationToken cancellationToken)
    {
        if ((before.GetDocument(id) ?? Sibling(workspace, before, id)) is not { } source)
            return null;

        var text = await source.GetTextAsync(cancellationToken).ConfigureAwait(false);

        return LineEndings.Uniform(text.ToString());
    }

    private static async Task<Solution> AdoptAsync(
        LoadedWorkspace workspace,
        Solution before,
        Solution solution,
        DocumentId id,
        CancellationToken cancellationToken)
    {
        if (solution.GetDocument(id) is not { } document)
            return solution;

        if (await EndingAsync(workspace, before, id, cancellationToken).ConfigureAwait(false) is not { } ending)
            return solution;

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var current = text.ToString();
        var adopted = LineEndings.Apply(current, ending);

        return string.Equals(current, adopted, StringComparison.Ordinal)
            ? solution
            : solution.WithDocumentText(id, SourceText.From(adopted, text.Encoding));
    }

    private static async Task<Solution> AdoptEndingsAsync(
        LoadedWorkspace workspace,
        Solution after,
        IReadOnlyList<DocumentId> changed,
        CancellationToken cancellationToken)
    {
        var solution = after;

        foreach (var id in changed)
            solution = await AdoptAsync(workspace, workspace.Solution, solution, id, cancellationToken).ConfigureAwait(false);

        return solution;
    }

    private static Document? Sibling(LoadedWorkspace workspace, Solution before, DocumentId id) => before
            .GetProject(id.ProjectId)?
            .Documents
            .FirstOrDefault(document => document.FilePath is { Length: > 0 } file
                && SourceFile.IsCSharp(file)
                && !GeneratedCode.IsGenerated(workspace.Root, file));

    private const int MaxImportHints = 3;

    private static bool IsMissingName(string key) =>
        key.StartsWith("CS0246 ", StringComparison.Ordinal) || key.StartsWith("CS0103 ", StringComparison.Ordinal);

    private static string? Quoted(string message)
    {
        var open = message.IndexOf('\'', StringComparison.Ordinal);
        var close = open < 0 ? -1 : message.IndexOf('\'', open + 1);

        if (close <= open + 1)
            return null;

        var name = message.AsSpan(open + 1, close - open - 1);
        var generic = name.IndexOf('<');

        return new string(generic < 0 ? name : name[..generic]);
    }

    private static async Task<string[]> NamespacesAsync(Project project, string name, CancellationToken cancellationToken)
    {
        var declarations = await SymbolFinder
            .FindDeclarationsAsync(project, name, ignoreCase: false, SymbolFilter.Type, cancellationToken)
            .ConfigureAwait(false);

        return [.. declarations
        .Where(symbol => symbol is { DeclaredAccessibility: Accessibility.Public, ContainingType: null, ContainingNamespace.IsGlobalNamespace: false })
        .Select(symbol => symbol.ContainingNamespace.ToDisplayString())
        .Distinct(StringComparer.Ordinal)];
    }

    private static async Task<string[]?> ImportHintAsync(
            Solution after,
            IReadOnlyList<DocumentId> changed,
            string root,
            string[] errors,
            CancellationToken cancellationToken)
    {
        if (errors.Length is 0)
            return null;

        var spaces = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var error in errors)
        {
            if (!IsMissingName(error) || Quoted(error) is not { Length: > 0 } name)
                return null;

            if (Erroring(after, root, error, changed) is not { } project)
                return null;

            if (await NamespacesAsync(project, name, cancellationToken).ConfigureAwait(false) is not [var only])
                return null;

            spaces.Add(only);
        }

        return spaces.Count > MaxImportHints ? null : [.. spaces];
    }

    private static Project? Edited(Solution after, IReadOnlyList<DocumentId> changed)
    {
        foreach (var id in changed)
        {
            if (after.GetDocument(id)?.Project is { } project)
                return project;
        }

        return null;
    }

    private static string? PathOf(string error)
    {
        var space = error.IndexOf(' ', StringComparison.Ordinal);
        var colon = space < 0 ? -1 : error.IndexOf(": ", space, StringComparison.Ordinal);

        return colon < 0 ? null : error[(space + 1)..colon];
    }

    private static Project? Erroring(Solution after, string root, string error, IReadOnlyList<DocumentId> changed)
    {
        if (PathOf(error) is { Length: > 0 } relative
            && after.GetDocumentIdsWithFilePath(Path.Combine(root, relative)) is [var id, ..]
            && after.GetDocument(id)?.Project is { } project)
        {
            return project;
        }

        return Edited(after, changed);
    }

    private const int MaxCallerHints = 5;

    private static bool IsCallShape(string key) =>
        key.StartsWith("CS7036 ", StringComparison.Ordinal)
        || key.StartsWith("CS1501 ", StringComparison.Ordinal)
        || key.StartsWith("CS1503 ", StringComparison.Ordinal)
        || key.StartsWith("CS1729 ", StringComparison.Ordinal);

    private static Document? Located(Solution after, string root, string error) =>
        PathOf(error) is { Length: > 0 } relative
        && after.GetDocumentIdsWithFilePath(Path.Combine(root, relative)) is [var id, ..]
            ? after.GetDocument(id)
            : null;

    private static string? Declaring(SyntaxNode syntax, SemanticModel model, TextSpan line, CancellationToken cancellationToken)
    {
        var containing = syntax.FindNode(line, getInnermostNodeForTie: true)
            .AncestorsAndSelf()
            .OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(node => node is not BaseTypeDeclarationSyntax);

        return containing is not null && model.GetDeclaredSymbol(containing, cancellationToken) is { } symbol
            ? SymbolLookup.Addressable(symbol)
            : null;
    }

    private static async Task<string?> ContainingAsync(
            Solution after,
            string root,
            string error,
            int line,
            CancellationToken cancellationToken)
    {
        if (Located(after, root, error) is not { } document)
            return null;

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);

        if (line < 1 || line > text.Lines.Count)
            return null;

        var syntax = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        return syntax is null || model is null
            ? null
            : Declaring(syntax, model, text.Lines[line - 1].Span, cancellationToken);
    }

    private static async Task<string[]?> CallerHintAsync(
            Solution after,
            string root,
            string[] errors,
            Dictionary<string, List<int>> lines,
            CancellationToken cancellationToken)
    {
        if (errors.Length is 0)
            return null;

        var callers = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var error in errors)
        {
            if (!IsCallShape(error) || !lines.TryGetValue(error, out var at))
                return null;

            if (!await GatherAsync(after, root, error, at, callers, cancellationToken).ConfigureAwait(false))
                return null;
        }

        return callers.Count is 0 or > MaxCallerHints ? null : [.. callers];
    }

    private static void Note(Dictionary<string, List<int>> lines, string key, int line)
    {
        if (!lines.TryGetValue(key, out var at))
            lines[key] = at = new List<int>(1);

        if (at.Count <= MaxCallerHints && !at.Contains(line))
            at.Add(line);
    }

    private static async Task<bool> GatherAsync(
        Solution after,
        string root,
        string error,
        List<int> lines,
        SortedSet<string> callers,
        CancellationToken cancellationToken)
    {
        foreach (var line in lines)
        {
            if (await ContainingAsync(after, root, error, line, cancellationToken).ConfigureAwait(false) is not { } caller)
                return false;

            callers.Add(caller);
        }

        return true;
    }

    private static TerseError? Blocked(GateReport? report, PolicyVerdict policy, string tool) => report switch
    {
        { NewErrors.Length: > 0 } => Errors.CompileRegression(report.NewErrors, report.Imports, report.Callers, report.Collisions, tool),
        _ => policy.Blocks ? Errors.PolicyViolation(policy) : null,
    };

    private static void Policy(ResponseBuilder response, PolicyVerdict policy, bool dryRun)
    {
        if (policy.Notice is { } notice)
            response.Line("WARNING " + notice);

        foreach (var finding in policy.Warned)
            response.Line(string.Create(CultureInfo.InvariantCulture, $"WARNING policy  {finding.Render()}"));

        if (policy.Bypassed)
        {
            foreach (var finding in policy.Rejected)
                response.Line(string.Create(CultureInfo.InvariantCulture, $"WARNING policy overridden  {finding.Render()}"));
        }
        else if (dryRun && policy.Blocks)
        {
            foreach (var finding in policy.Rejected)
                response.Line(string.Create(CultureInfo.InvariantCulture, $"WARNING would be rolled back by policy  {finding.Explain()}"));
        }
    }

    private static string[]? Collided(string[] errors, ImmutableArray<string> usings)
    {
        if (errors.Length is 0 || usings.IsDefaultOrEmpty || !errors.All(IsAmbiguity))
            return null;

        var blamed = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var space in usings)
        {
            if (errors.Any(error => Names(error, space)))
                blamed.Add(space);
        }

        return blamed.Count is 0 ? null : [.. blamed];
    }

    private static bool IsAmbiguity(string error) => error.StartsWith("CS0104", StringComparison.Ordinal);

    private static bool Names(string error, string space) => error.Contains("'" + space + ".", StringComparison.Ordinal);
}
