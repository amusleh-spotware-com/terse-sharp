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
        var collected = await CollectedAsync(workspace, path, includeDeadCode, changed, ids, cancellationToken).ConfigureAwait(false);

        if (!collected.IsOk)
            return collected.Error!.Render();

        var value = collected.Value;

        return Render(
            workspace.Root,
            path,
            value,
            Filter(value.Found, value.Scope, minimum, ids),
            maxResults,
            sinceLast,
            minimum,
            ids,
            includeDeadCode,
            changed);
    }

    public static async Task<Result<string[]>> FindingsAsync(
        LoadedWorkspace workspace,
        string? path,
        DiagnosticSeverity minimum,
        bool includeDeadCode,
        bool changed,
        CancellationToken cancellationToken)
    {
        var collected = await CollectedAsync(workspace, path, includeDeadCode, changed, [], cancellationToken).ConfigureAwait(false);

        return collected.IsOk
            ? Result.Ok(Grouped(
                DiagnosticFold.Findings(workspace.Root, Filter(collected.Value.Found, collected.Value.Scope, minimum, []), DiagnosticFormat.Head),
                collected.Value.Extra))
            : Result.Fail<string[]>(collected.Error!);
    }

    private static async Task<Result<Collected>> CollectedAsync(
        LoadedWorkspace workspace,
        string? path,
        bool includeDeadCode,
        bool changed,
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken)
    {
        var unscoped = path is null && !changed;
        var documents = unscoped ? [] : DocumentScope.Select(workspace, path, changed);

        if (!unscoped && documents.Length is 0)
            return Result.Fail<Collected>(Empty(path, changed));

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

        return Result.Ok(new Collected(found, analyzed, extra, ProjectDiagnostics.Unsupported(targets, ids, found), scope));
    }

    private readonly record struct Collected(
        ConcurrentBag<Diagnostic> Found,
        ConcurrentBag<string> Analyzed,
        IReadOnlyList<string> Extra,
        IReadOnlyList<string> Unsupported,
        DiagnosticScope Scope);

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

    private static string[] Grouped(DiagnosticFold.Finding[] findings, IReadOnlyList<string> extra) =>
    [
        .. DiagnosticFold
            .Lines(findings)
            .Concat(extra
                .GroupBy(text => text, StringComparer.Ordinal)
                .Select(group => DiagnosticFold.Repeated(group.Key, group.Count())))
            .Order(StringComparer.Ordinal),
    ];

    private static string Render(
        string root,
        string? path,
        Collected collected,
        Diagnostic[] found,
        int maxResults,
        bool sinceLast,
        DiagnosticSeverity minimum,
        IReadOnlyList<string> ids,
        bool includeDeadCode,
        bool changed)
    {
        var extra = Keep(collected.Extra, ids);
        var findings = DiagnosticFold.Findings(root, found, DiagnosticFormat.Head);
        var occurrences = Occurrences(findings, extra);
        var scope = string.Create(CultureInfo.InvariantCulture, $"analyze|{root}|{path ?? "solution"}|{changed}|{minimum}|{string.Join(",", ids)}|{includeDeadCode}");
        var delta = DiagnosticHistory.Record(scope, occurrences);
        var shown = sinceLast ? delta.Appeared : Grouped(findings, extra);

        var response = new ResponseBuilder("analyze", path ?? "solution");

        response.Summary(
            ResultCap.Shown(shown.Count, maxResults),
            shown.Count,
            sinceLast ? "new diagnostics, one record per occurrence" : "diagnostics, one record per id and message",
            "minSeverity=, ids= or path=");
        response.Note("engines=" + string.Join("+", Engines(collected.Analyzed, includeDeadCode)));

        if (!sinceLast && occurrences.Length != shown.Count)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"total={occurrences.Length} occurrence(s) folded onto {shown.Count} record(s)"));

        if (collected.Unsupported.Count > 0)
            response.Note("NOT_ENABLED " + string.Join(", ", collected.Unsupported) + " - no analyzer these projects reference declares it, so this pass could not have found it");

        if (changed)
            response.Note("gate runs this, format and cleanup fix=all as one call");

        if (sinceLast)
        {
            response.Note(delta.Baseline
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"no previous analyze of this scope: this run is the baseline, all {occurrences.Length} occurrence(s) are listed")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"since the previous analyze of this scope: appeared={delta.Appeared.Count} fixed={delta.Fixed.Count} unchanged={delta.Unchanged} total={occurrences.Length} occurrence(s)"));
        }

        foreach (var line in shown.Capped(maxResults))
            response.Line(line);

        foreach (var line in sinceLast ? delta.Fixed.Capped(maxResults) : [])
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
            "no document under that scope was modified since this workspace started tracking changes",
            "drop changed=true to analyze the whole scope, or pass path= to name the files yourself")
        : Errors.DocumentNotFound(path ?? "solution");

    private static string[] Occurrences(DiagnosticFold.Finding[] findings, IReadOnlyList<string> extra) =>
        DiagnosticFold.PerOccurrence(findings.Select(finding => finding.Key).Concat(extra));
}

public static class DiagnosticFormat
{
    public static string Head(Diagnostic diagnostic) => string.Create(
        CultureInfo.InvariantCulture,
        $"{diagnostic.Id} {Severity(diagnostic)} {Category(diagnostic)}");

    private static string Severity(Diagnostic diagnostic) => diagnostic.Severity.ToString().ToLowerInvariant();

    private static string Category(Diagnostic diagnostic) =>
        string.IsNullOrEmpty(diagnostic.Descriptor.Category) ? "-" : diagnostic.Descriptor.Category;
}
