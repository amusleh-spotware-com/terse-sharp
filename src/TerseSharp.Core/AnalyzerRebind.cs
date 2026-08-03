using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TerseSharp.Core;

public static class AnalyzerRebind
{
    public static Solution Rebound(Solution solution, IAnalyzerAssemblyLoader loader)
    {
        var rebound = solution;

        foreach (var project in solution.Projects)
        {
            if (Rebind(project, loader) is { } references)
                rebound = rebound.WithProjectAnalyzerReferences(project.Id, references);
        }

        return rebound;
    }

    private static AnalyzerReference[]? Rebind(Project project, IAnalyzerAssemblyLoader loader) =>
        project.AnalyzerReferences.Any(reference => reference is AnalyzerFileReference)
            ? [.. project.AnalyzerReferences.Select(reference => Bound(reference, loader))]
            : null;

    private static AnalyzerReference Bound(AnalyzerReference reference, IAnalyzerAssemblyLoader loader) =>
        reference is AnalyzerFileReference { FullPath: { Length: > 0 } path }
            ? new AnalyzerFileReference(path, loader)
            : reference;
}
