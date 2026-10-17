using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TerseSharp.Core;

internal static class SuppressedDiagnostics
{
    public static async Task<ImmutableArray<Diagnostic>> CompilerAsync(Project project, Compilation compilation, CancellationToken cancellationToken)
    {
        var reported = compilation.GetDiagnostics(cancellationToken);
        var suppressors = Suppressing(project, reported);

        if (suppressors.IsEmpty)
            return reported;

        var options = new CompilationWithAnalyzersOptions(
            project.AnalyzerOptions,
            onAnalyzerException: null,
            concurrentAnalysis: true,
            logAnalyzerExecutionTime: false);

        return await compilation.WithAnalyzers(suppressors, options).GetAllDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ImmutableArray<DiagnosticAnalyzer> Suppressing(Project project, ImmutableArray<Diagnostic> reported)
    {
        if (reported.IsEmpty)
            return [];

        var ids = new HashSet<string>(reported.Length, StringComparer.Ordinal);

        foreach (var diagnostic in reported)
            ids.Add(diagnostic.Id);

        return [.. ProjectDiagnostics.Analyzers(project).OfType<DiagnosticSuppressor>().Where(suppressor => Suppresses(suppressor, ids))];
    }

    private static bool Suppresses(DiagnosticSuppressor suppressor, HashSet<string> ids)
    {
        try
        {
            return suppressor.SupportedSuppressions.Any(descriptor => ids.Contains(descriptor.SuppressedDiagnosticId));
        }
        catch (Exception exception) when (exception is TypeLoadException or MissingMemberException or FileNotFoundException)
        {
            return false;
        }
    }
}
