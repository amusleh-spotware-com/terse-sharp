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

    public static UnresolvedAnalyzers Unresolved(Solution solution)
    {
        var paths = new List<string>();
        var projects = 0;

        foreach (var project in solution.Projects)
        {
            var before = paths.Count;

            foreach (var reference in project.AnalyzerReferences)
            {
                if (Unresolvable(reference))
                    paths.Add(reference.FullPath!);
            }

            if (paths.Count > before)
                projects++;
        }

        return paths.Count is 0 ? UnresolvedAnalyzers.None : new UnresolvedAnalyzers(projects, paths);
    }

    private static AnalyzerReference[]? Rebind(Project project, IAnalyzerAssemblyLoader loader) =>
        project.AnalyzerReferences.Any(reference => reference is AnalyzerFileReference || Unresolvable(reference))
            ? [.. project.AnalyzerReferences.Where(reference => !Unresolvable(reference)).Select(reference => Bound(reference, loader))]
            : null;

    private static bool Unresolvable(AnalyzerReference reference) =>
        reference is not AnalyzerFileReference
        && reference.FullPath is { Length: > 0 } path
        && !File.Exists(path);

    private static AnalyzerReference Bound(AnalyzerReference reference, IAnalyzerAssemblyLoader loader) =>
        reference is AnalyzerFileReference { FullPath: { Length: > 0 } path }
            ? new AnalyzerFileReference(path, loader)
            : reference;
}
