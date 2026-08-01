using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class AnalysisService
{
    public static async Task<string> AnalyzeAsync(
        LoadedWorkspace workspace,
        string? path,
        DiagnosticSeverity minimum,
        IReadOnlyList<string> ids,
        bool includeDeadCode,
        int maxResults,
        bool sinceLast,
        bool changed,
        CancellationToken cancellationToken)
    {
        var unscoped = path is null && !changed;
        var documents = unscoped ? [] : DocumentScope.Select(workspace, path, changed);

        if (!unscoped && documents.Length is 0)
            return Empty(path, changed).Render();

        var targets = Targets(workspace, documents, unscoped).ToArray();
        var scope = Scope(workspace, documents, unscoped);
        var found = new ConcurrentBag<Diagnostic>();
        var analyzed = new ConcurrentBag<string>();

        await Parallel.ForEachAsync(
            targets,
            ParallelWork.Options(cancellationToken),
            (project, token) => CollectAsync(project, found, analyzed, token)).ConfigureAwait(false);

        var extra = includeDeadCode
            ? await DeadCodeService.FindAsync(workspace, targets, scope, cancellationToken).ConfigureAwait(false)
            : [];

        return Render(
            workspace.Root,
            path,
            Engines(analyzed, includeDeadCode),
            Filter(found, scope, minimum, ids),
            Keep(extra, ids),
            maxResults,
            sinceLast,
            minimum,
            ids,
            includeDeadCode,
            changed);
    }

    private static List<string> Engines(ConcurrentBag<string> analyzed, bool includeDeadCode)
    {
        var engines = new List<string>(analyzed.Count + 2) { "compiler" };

        engines.AddRange(analyzed.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

        if (includeDeadCode)
            engines.Add("dead-code");

        return engines;
    }

    private static string[] Keep(IReadOnlyList<string> findings, IReadOnlyList<string> ids) =>
        ids.Count is 0
            ? [.. findings]
            : [.. findings.Where(finding => ids.Any(id => finding.StartsWith(id, StringComparison.OrdinalIgnoreCase)))];

    private static IEnumerable<Project> Targets(LoadedWorkspace workspace, DocumentId[] documents, bool unscoped) =>
        unscoped
            ? workspace.Solution.Projects
            : documents
                .Select(id => workspace.Solution.GetDocument(id)?.Project)
                .OfType<Project>()
                .DistinctBy(project => project.Id);

    private static async ValueTask CollectAsync(
        Project project,
        ConcurrentBag<Diagnostic> found,
        ConcurrentBag<string> analyzed,
        CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

        if (compilation is null)
            return;

        foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken))
            found.Add(diagnostic);

        var analyzers = ProjectDiagnostics.Analyzers(project);

        if (analyzers.IsEmpty)
            return;

        analyzed.Add("analyzers(" + project.Name + ")");

        foreach (var diagnostic in await ProjectDiagnostics.RunAsync(compilation, project, analyzers, cancellationToken).ConfigureAwait(false))
            found.Add(diagnostic);
    }

    private static Diagnostic[] Filter(
        ConcurrentBag<Diagnostic> found,
        DiagnosticScope scope,
        DiagnosticSeverity minimum,
        IReadOnlyList<string> ids) =>
        [.. found.Where(diagnostic => Keep(diagnostic, scope, minimum, ids))];

    private static bool Keep(Diagnostic diagnostic, DiagnosticScope scope, DiagnosticSeverity minimum, IReadOnlyList<string> ids) =>
        diagnostic.Severity >= minimum
        && !diagnostic.IsSuppressed
        && (ids.Count is 0 || ids.Contains(diagnostic.Id, StringComparer.OrdinalIgnoreCase))
        && scope.Includes(diagnostic);

    private static string Render(
        string root,
        string? path,
        List<string> engines,
        Diagnostic[] found,
        string[] extra,
        int maxResults,
        bool sinceLast,
        DiagnosticSeverity minimum,
        IReadOnlyList<string> ids,
        bool includeDeadCode,
        bool changed)
    {
        var grouped = found
            .Select(diagnostic => DiagnosticFormat.Key(root, diagnostic))
            .Concat(extra)
            .GroupBy(text => text, StringComparer.Ordinal)
            .Select(group => new { Text = group.Key, Count = group.Count() })
            .OrderBy(entry => entry.Text, StringComparer.Ordinal)
            .ToArray();

        var lines = grouped
            .Select(entry => entry.Count is 1 ? entry.Text : string.Create(CultureInfo.InvariantCulture, $"{entry.Text} x{entry.Count}"))
            .ToArray();

        var scope = string.Create(CultureInfo.InvariantCulture, $"analyze|{root}|{path ?? "solution"}|{changed}|{minimum}|{string.Join(",", ids)}|{includeDeadCode}");
        var delta = DiagnosticHistory.Record(scope, lines);
        var shown = sinceLast ? delta.Appeared : lines;

        var response = new ResponseBuilder("analyze", path ?? "solution");

        response.Summary(
            Math.Min(maxResults, shown.Count),
            shown.Count,
            sinceLast ? "new diagnostics" : "diagnostics",
            "minSeverity=, ids= or path=");
        response.Note("engines=" + string.Join("+", engines));

        if (sinceLast)
        {
            response.Note(delta.Baseline
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"no previous analyze of this scope: this run is the baseline, all {lines.Length} diagnostic(s) are listed")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"since the previous analyze of this scope: appeared={delta.Appeared.Count} fixed={delta.Fixed.Count} unchanged={delta.Unchanged} total={lines.Length}"));
        }

        foreach (var line in shown.Take(maxResults))
            response.Line(line);

        foreach (var line in sinceLast ? delta.Fixed.Take(maxResults) : [])
            response.Line("FIXED " + line);

        return response.ToString();
    }

    private static DiagnosticScope Scope(LoadedWorkspace workspace, DocumentId[] documents, bool unscoped) =>
            unscoped
                ? DiagnosticScope.For(workspace, null)
                : DiagnosticScope.Of(
                    workspace.Root,
                    documents.Select(id => workspace.Solution.GetDocument(id)?.FilePath).OfType<string>());

    private static TerseError Empty(string? path, bool changed) => changed
            ? Errors.Invalid(
                "no document under that scope was modified since the workspace loaded",
                "drop changed=true to analyze the whole scope")
            : Errors.DocumentNotFound(path ?? "solution");
}

public static class DiagnosticFormat
{
    public static string Key(string root, Diagnostic diagnostic) => string.Create(
        CultureInfo.InvariantCulture,
        $"{diagnostic.Id} {Severity(diagnostic)} {Category(diagnostic)} {PositionFormat.Describe(root, diagnostic.Location)}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}");

    private static string Severity(Diagnostic diagnostic) => diagnostic.Severity.ToString().ToLowerInvariant();

    private static string Category(Diagnostic diagnostic) =>
        string.IsNullOrEmpty(diagnostic.Descriptor.Category) ? "-" : diagnostic.Descriptor.Category;
}
