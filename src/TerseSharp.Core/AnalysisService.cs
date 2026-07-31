using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

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
        CancellationToken cancellationToken)
    {
        var scope = DiagnosticScope.For(workspace, path);
        var document = path is null ? null : DocumentLookup.Find(workspace, path);
        var found = new ConcurrentBag<Diagnostic>();
        var analyzed = new ConcurrentBag<string>();

        await Parallel.ForEachAsync(
            Targets(workspace, document),
            ParallelWork.Options(cancellationToken),
            (project, token) => CollectAsync(project, found, analyzed, token)).ConfigureAwait(false);

        var extra = includeDeadCode
            ? await DeadCodeService.FindAsync(workspace, path, cancellationToken).ConfigureAwait(false)
            : [];

        return Render(
            workspace.Root,
            path,
            Engines(analyzed, includeDeadCode),
            Filter(found, scope, minimum, ids),
            Keep(extra, ids),
            maxResults);
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

    private static IEnumerable<Project> Targets(LoadedWorkspace workspace, Document? document) =>
        document is null ? workspace.Solution.Projects : [document.Project];

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

        var analyzers = Analyzers(project);

        if (analyzers.IsEmpty)
            return;

        analyzed.Add("analyzers(" + project.Name + ")");

        foreach (var diagnostic in await RunAsync(compilation, project, analyzers, cancellationToken).ConfigureAwait(false))
            found.Add(diagnostic);
    }

    private static async Task<ImmutableArray<Diagnostic>> RunAsync(
        Compilation compilation,
        Project project,
        ImmutableArray<DiagnosticAnalyzer> analyzers,
        CancellationToken cancellationToken)
    {
        var options = new CompilationWithAnalyzersOptions(
            project.AnalyzerOptions,
            onAnalyzerException: null,
            concurrentAnalysis: true,
            logAnalyzerExecutionTime: false);

        var withAnalyzers = compilation.WithAnalyzers(analyzers, options);

        return await withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ImmutableArray<DiagnosticAnalyzer> Analyzers(Project project) =>
        [.. project.AnalyzerReferences.SelectMany(reference => reference.GetAnalyzers(project.Language))];

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
        int maxResults)
    {
        var grouped = found
            .Select(diagnostic => DiagnosticFormat.Key(root, diagnostic))
            .Concat(extra)
            .GroupBy(text => text, StringComparer.Ordinal)
            .Select(group => new { Text = group.Key, Count = group.Count() })
            .OrderBy(entry => entry.Text, StringComparer.Ordinal)
            .ToArray();

        var response = new ResponseBuilder("analyze", path ?? "solution");

        response.Summary(Math.Min(maxResults, grouped.Length), grouped.Length, "diagnostics");
        response.Note("engines=" + string.Join("+", engines));

        foreach (var entry in grouped.Take(maxResults))
            response.Line(entry.Count is 1 ? entry.Text : string.Create(CultureInfo.InvariantCulture, $"{entry.Text} x{entry.Count}"));

        return response.ToString();
    }
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
