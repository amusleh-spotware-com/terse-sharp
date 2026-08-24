using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TerseSharp.Core;

public static class MappedAnalyzers
{
    public static string[] Of(Solution solution, string root)
    {
        var referenced = Referenced(solution, root);

        return referenced.Count is 0 ? [] : Loaded(referenced);
    }

    private static HashSet<string> Referenced(Solution solution, string root)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.Projects)
        {
            foreach (var reference in project.AnalyzerReferences.OfType<AnalyzerFileReference>())
            {
                if (reference.FullPath is { Length: > 0 } path && Path.GetFullPath(path) is var full && PathBoundary.Contains(root, full))
                    paths.Add(full);
            }
        }

        return paths;
    }

    private static string[] Loaded(HashSet<string> referenced)
    {
        var mapped = new List<string>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (Location(assembly) is { Length: > 0 } location && referenced.Contains(location))
                mapped.Add(location);
        }

        mapped.Sort(StringComparer.Ordinal);

        return [.. mapped];
    }

    private static string? Location(Assembly assembly)
    {
        try
        {
            return assembly.IsDynamic ? null : assembly.Location;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
