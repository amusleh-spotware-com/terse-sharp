using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TerseSharp.Core;

internal static class ProjectDiagnostics
{
    public static ImmutableArray<DiagnosticAnalyzer> Analyzers(Project project) =>
        [.. project.AnalyzerReferences.SelectMany(reference => reference.GetAnalyzers(project.Language))];

    public static async Task<ImmutableArray<Diagnostic>> RunAsync(
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

        return await compilation
            .WithAnalyzers(analyzers, options)
            .GetAnalyzerDiagnosticsAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<ImmutableArray<Diagnostic>> OfProjectAsync(Project project, CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        var analyzers = Analyzers(project);

        return compilation is null || analyzers.IsEmpty
            ? []
            : await RunAsync(compilation, project, analyzers, cancellationToken).ConfigureAwait(false);
    }

    private static bool Produces(DiagnosticAnalyzer analyzer, HashSet<string> ids) =>
        Supported(analyzer).Any(descriptor => ids.Contains(descriptor.Id));

    public static ImmutableArray<DiagnosticAnalyzer> Producing(Project project, IReadOnlyCollection<string> ids)
    {
        if (ids.Count is 0)
            return Analyzers(project);

        var wanted = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);

        return [.. Analyzers(project).Where(analyzer => Produces(analyzer, wanted))];
    }

    public static async Task<ImmutableArray<Diagnostic>> OfProjectAsync(
        Project project,
        IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        var analyzers = Producing(project, ids);

        return compilation is null || analyzers.IsEmpty
            ? []
            : await RunAsync(compilation, project, analyzers, cancellationToken).ConfigureAwait(false);
    }

    private static ImmutableArray<DiagnosticDescriptor> Supported(DiagnosticAnalyzer analyzer)
    {
        try
        {
            return analyzer.SupportedDiagnostics;
        }
        catch (Exception exception) when (exception is TypeLoadException or MissingMemberException or FileNotFoundException)
        {
            return [];
        }
    }

    public static string[] Unsupported(IEnumerable<Project> projects, IReadOnlyList<string> ids, IEnumerable<Diagnostic> found)
    {
        if (ids.Count is 0)
            return [];

        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            foreach (var analyzer in Analyzers(project))
            {
                foreach (var descriptor in Supported(analyzer))
                    declared.Add(descriptor.Id);
            }
        }

        foreach (var diagnostic in found)
            declared.Add(diagnostic.Id);

        return [.. ids.Where(id => !declared.Contains(id) && !IsCompilerId(id) && !IsDeadCodeId(id))];
    }

    private static bool IsCompilerId(string id) =>
        id.Length > 2
        && id.StartsWith("CS", StringComparison.OrdinalIgnoreCase)
        && !id.AsSpan(2).ContainsAnyExceptInRange('0', '9');


    private static bool IsDeadCodeId(string id) =>
        string.Equals(id, DeadCodeService.RuleId, StringComparison.OrdinalIgnoreCase);
}
