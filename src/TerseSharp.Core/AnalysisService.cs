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
        var found = new List<Diagnostic>();
        var engines = new List<string> { "compiler" };
        var scope = DiagnosticScope.For(workspace, path);
        var document = path is null ? null : DocumentLookup.Find(workspace, path);

        foreach (var project in Targets(workspace, document))
            await CollectAsync(project, found, engines, cancellationToken).ConfigureAwait(false);

        var extra = includeDeadCode
            ? await DeadCodeService.FindAsync(workspace, path, cancellationToken).ConfigureAwait(false)
            : [];

        if (includeDeadCode)
            engines.Add("dead-code");

        return Render(path, engines, Filter(found, scope, minimum, ids), Keep(extra, ids), maxResults);
    }

    private static string[] Keep(IReadOnlyList<string> findings, IReadOnlyList<string> ids) =>
        ids.Count is 0
            ? [.. findings]
            : [.. findings.Where(finding => ids.Any(id => finding.StartsWith(id, StringComparison.OrdinalIgnoreCase)))];

    private static IEnumerable<Project> Targets(LoadedWorkspace workspace, Document? document) =>
        document is null ? workspace.Solution.Projects : [document.Project];

    private static async Task CollectAsync(
        Project project,
        List<Diagnostic> found,
        List<string> engines,
        CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

        if (compilation is null)
            return;

        found.AddRange(compilation.GetDiagnostics(cancellationToken));

        var analyzers = Analyzers(project);

        if (analyzers.IsEmpty)
            return;

        AddEngine(engines, project);
        found.AddRange(await RunAsync(compilation, project, analyzers, cancellationToken).ConfigureAwait(false));
    }

    private static void AddEngine(List<string> engines, Project project)
    {
        var name = "analyzers(" + project.Name + ")";

        if (!engines.Contains(name, StringComparer.Ordinal))
            engines.Add(name);
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
        List<Diagnostic> found,
        DiagnosticScope scope,
        DiagnosticSeverity minimum,
        IReadOnlyList<string> ids) =>
        [.. found.Where(diagnostic => Keep(diagnostic, scope, minimum, ids))];

    private static bool Keep(Diagnostic diagnostic, DiagnosticScope scope, DiagnosticSeverity minimum, IReadOnlyList<string> ids) =>
        diagnostic.Severity >= minimum
        && !diagnostic.IsSuppressed
        && (ids.Count is 0 || ids.Contains(diagnostic.Id, StringComparer.OrdinalIgnoreCase))
        && scope.Includes(diagnostic);

    private static string Render(string? path, List<string> engines, Diagnostic[] found, string[] extra, int maxResults)
    {
        var grouped = found
            .Select(DiagnosticFormat.Key)
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
    public static string Key(Diagnostic diagnostic) => string.Create(
        CultureInfo.InvariantCulture,
        $"{diagnostic.Id} {Severity(diagnostic)} {Category(diagnostic)} {PositionFormat.Describe(diagnostic.Location)}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}");

    private static string Severity(Diagnostic diagnostic) => diagnostic.Severity.ToString().ToLowerInvariant();

    private static string Category(Diagnostic diagnostic) =>
        string.IsNullOrEmpty(diagnostic.Descriptor.Category) ? "-" : diagnostic.Descriptor.Category;
}
