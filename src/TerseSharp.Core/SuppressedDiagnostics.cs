using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TerseSharp.Core;

internal static class SuppressedDiagnostics
{
    private static readonly ConditionalWeakTable<IReadOnlyList<AnalyzerReference>, DiagnosticSuppressor[]> SuppressorsByReferences = [];
    private static readonly ConditionalWeakTable<Compilation, Task<ImmutableArray<Diagnostic>>> SuppressedByCompilation = [];

    public static async Task<ImmutableArray<Diagnostic>> CompilerAsync(Project project, Compilation compilation, CancellationToken cancellationToken)
    {
        var reported = compilation.GetDiagnostics(cancellationToken);
        var suppressors = Suppressing(project, reported);

        if (suppressors.IsEmpty)
            return reported;

        var suppressed = SuppressedByCompilation.GetValue(compilation, _ => RunAsync(project, compilation, suppressors));

        return await suppressed.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Task<ImmutableArray<Diagnostic>> RunAsync(Project project, Compilation compilation, ImmutableArray<DiagnosticAnalyzer> suppressors)
    {
        var options = new CompilationWithAnalyzersOptions(
            project.AnalyzerOptions,
            onAnalyzerException: null,
            concurrentAnalysis: true,
            logAnalyzerExecutionTime: false);

        return compilation.WithAnalyzers(suppressors, options).GetAllDiagnosticsAsync(CancellationToken.None);
    }

    private static ImmutableArray<DiagnosticAnalyzer> Suppressing(Project project, ImmutableArray<Diagnostic> reported)
    {
        if (reported.IsEmpty)
            return [];

        var declared = SuppressorsByReferences.GetValue(project.AnalyzerReferences, _ => [.. ProjectDiagnostics.Analyzers(project).OfType<DiagnosticSuppressor>()]);

        if (declared.Length is 0)
            return [];

        var ids = new HashSet<string>(reported.Length, StringComparer.Ordinal);

        foreach (var diagnostic in reported)
            ids.Add(diagnostic.Id);

        return [.. declared.Where(suppressor => Suppresses(suppressor, ids))];
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
