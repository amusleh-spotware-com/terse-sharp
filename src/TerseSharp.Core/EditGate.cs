using Microsoft.CodeAnalysis;

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
        var diff = await DiffAsync(workspace.Solution, updated, changed, cancellationToken).ConfigureAwait(false);

        var report = options.AllowErrors
            ? null
            : await AnalyseAsync(workspace.Solution, updated, changed, cancellationToken).ConfigureAwait(false);

        if (options.DryRun)
            return Result.Ok(Render(options.Tool, diff, "dryRun", report));

        if (report is { NewErrors.Length: > 0 })
            return Result.Fail<string>(Errors.CompileRegression(report.NewErrors));

        return workspace.TryApply(updated)
            ? Result.Ok(Render(options.Tool, diff, "applied", report))
            : Result.Fail<string>(Errors.EditConflict("the workspace rejected the change"));
    }

    private static string Render(string tool, DocumentDiff[] diffs, string state, GateReport? report)
    {
        var response = new ResponseBuilder(tool, state);

        response.Summary(diffs.Length, diffs.Length, "files changed");

        if (report is not null)
            Announce(response, report);

        foreach (var diff in diffs)
            response.Line(diff.Text).Line(string.Create(CultureInfo.InvariantCulture, $"changedLines={diff.ChangedLines}"));

        return response.ToString();
    }

    private static void Announce(ResponseBuilder response, GateReport report)
    {
        response.Note(Describe(report));

        if (report.NewErrors.Length is 0)
            return;

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"WARNING this edit introduces {report.NewErrors.Length} new error(s) and would be rolled back"));

        foreach (var error in report.NewErrors)
            response.Note(error);
    }

    private static string Describe(GateReport report) => string.Create(
        CultureInfo.InvariantCulture,
        $"errors={report.Errors} ({Signed(report.ErrorDelta)}) warnings={report.Warnings} ({Signed(report.WarningDelta)})");

    private static string Signed(int delta) =>
        delta >= 0 ? "+" + delta.ToString(CultureInfo.InvariantCulture) : delta.ToString(CultureInfo.InvariantCulture);

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
}
