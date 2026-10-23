using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class UnrestoredProjects
{
    public static string[] Find(Solution solution) => Find(solution, IsUnrestored);

    public static string[] Find(Solution solution, Func<string, bool> isUnrestored)
    {
        var seen = new HashSet<string>(StringComparer.FromComparison(PathBoundary.Comparison));
        var missing = new List<string>();

        foreach (var project in solution.Projects)
        {
            if (project.FilePath is { Length: > 0 } path && seen.Add(path) && isUnrestored(path))
                missing.Add(path);
        }

        return [.. missing];
    }

    public static bool IsUnrestored(string projectPath) =>
        !AssetsPresent(projectPath) && ProjectGlobs.Memoized(projectPath);

    public static bool AssetsPresent(string projectPath) =>
        File.Exists(Path.Combine(Path.GetDirectoryName(projectPath) ?? string.Empty, "obj", "project.assets.json"));
}
