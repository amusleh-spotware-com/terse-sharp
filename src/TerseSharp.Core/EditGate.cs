using Microsoft.CodeAnalysis;
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
            : await AnalyseAsync(workspace.Solution, adopted, changed, cancellationToken).ConfigureAwait(false);

        if (options.DryRun)
            return Result.Ok(Render(options, diff, "dryRun", report, workspace.Root));

        if (report is { NewErrors.Length: > 0 })
            return Result.Fail<string>(Errors.CompileRegression(report.NewErrors));

        return await workspace.TryApplyAsync(adopted, changed, cancellationToken).ConfigureAwait(false)
            ? Result.Ok(Render(options, diff, "applied", report, workspace.Root))
            : Result.Fail<string>(Errors.EditConflict("the workspace rejected the change"));
    }

    private static string Render(EditOptions options, DocumentDiff[] diffs, string state, GateReport? report, string root)
    {
        var response = new ResponseBuilder(options.Tool, state).Verbose(options.Verbose);

        response.Summary(diffs.Length, diffs.Length, "files changed");

        if (options.DryRun && !options.Verbose)
            response.Note("dryRun");

        if (report is not null)
            Announce(response, report, options.Verbose || options.DryRun);

        if (Condensed(options, diffs, report))
            return Compact(response, diffs, root);

        foreach (var diff in diffs)
            response.Line(diff.Text).Line(string.Create(CultureInfo.InvariantCulture, $"changedLines={diff.ChangedLines}"));

        return response.ToString();
    }
    private static void Announce(ResponseBuilder response, GateReport report, bool verbose)
    {
        if (Describe(report, verbose) is { Length: > 0 } counters)
            response.Note(counters);

        if (report.NewErrors.Length is 0)
            return;

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"WARNING this edit introduces {report.NewErrors.Length} new error(s) and would be rolled back"));

        foreach (var error in report.NewErrors)
            response.Note(error);
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
        CancellationToken cancellationToken)
    {
        var projects = Affected(before, changed);
        var baseline = await TallyAsync(before, projects, cancellationToken).ConfigureAwait(false);
        var current = await TallyAsync(after, projects, cancellationToken).ConfigureAwait(false);

        return new GateReport(
            [.. current.Errors.Where(entry => Appeared(baseline.Errors, entry)).Select(entry => entry.Key).Take(10)],
            current.ErrorCount,
            current.ErrorCount - baseline.ErrorCount,
            current.WarningCount,
            current.WarningCount - baseline.WarningCount);
    }

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
        CancellationToken cancellationToken)
    {
        var tally = new Tally(new Dictionary<string, int>(StringComparer.Ordinal), 0, 0);

        foreach (var projectId in projects)
        {
            var compilation = await Compile(solution, projectId, cancellationToken).ConfigureAwait(false);

            if (compilation is not null)
                tally = Collect(tally, compilation.GetDiagnostics(cancellationToken));
        }

        return tally;
    }

    private static Tally Collect(Tally tally, IEnumerable<Diagnostic> diagnostics)
    {
        var errors = tally.ErrorCount;
        var warnings = tally.WarningCount;

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity is DiagnosticSeverity.Error)
                errors += Record(tally.Errors, diagnostic);

            if (diagnostic.Severity is DiagnosticSeverity.Warning)
                warnings++;
        }

        return tally with { ErrorCount = errors, WarningCount = warnings };
    }

    private static int Record(Dictionary<string, int> errors, Diagnostic diagnostic)
    {
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{diagnostic.Id} {diagnostic.Location.GetLineSpan().Path}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}");

        errors[key] = errors.GetValueOrDefault(key) + 1;

        return 1;
    }

    private static Task<Compilation?> Compile(Solution solution, ProjectId projectId, CancellationToken cancellationToken)
    {
        var project = solution.GetProject(projectId);

        return project is null ? Task.FromResult<Compilation?>(null) : project.GetCompilationAsync(cancellationToken);
    }

    private sealed record GateReport(string[] NewErrors, int Errors, int ErrorDelta, int Warnings, int WarningDelta);

    private readonly record struct Tally(Dictionary<string, int> Errors, int ErrorCount, int WarningCount);
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

    private static bool Condensed(EditOptions options, DocumentDiff[] diffs, GateReport? report) =>
        !options.Verbose
        && !options.DryRun
        && diffs.Length is not 0
        && report is not { NewErrors.Length: > 0 };

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
}
