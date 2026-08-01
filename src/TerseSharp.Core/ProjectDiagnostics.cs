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

    private static bool Produces(DiagnosticAnalyzer analyzer, IReadOnlyCollection<string> ids)
    {
        try
        {
            return analyzer.SupportedDiagnostics.Any(descriptor => ids.Contains(descriptor.Id));
        }
        catch (Exception exception) when (exception is TypeLoadException or MissingMemberException or FileNotFoundException)
        {
            return false;
        }
    }

    public static ImmutableArray<DiagnosticAnalyzer> Producing(Project project, IReadOnlyCollection<string> ids) =>
        ids.Count is 0
            ? Analyzers(project)
            : [.. Analyzers(project).Where(analyzer => Produces(analyzer, ids))];

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
}
