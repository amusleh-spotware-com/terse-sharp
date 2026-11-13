using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public readonly record struct SearchScope(string Under, string Label)
{
    public static SearchScope? Resolve(LoadedWorkspace workspace, string path)
    {
        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(workspace.Root, path));

        if (Directory.Exists(full) && PathBoundary.Contains(workspace.Root, full))
            return ScopeOf(workspace, full);

        return ProjectNamed(workspace, path, full)?.FilePath is { } project && Path.GetDirectoryName(project) is { Length: > 0 } directory
            ? ScopeOf(workspace, directory)
            : null;
    }

    public static string? Closest(LoadedWorkspace workspace, string path)
    {
        var segment = Path.GetFileName(Path.TrimEndingDirectorySeparator(path.AsSpan()));
        string? closest = null;
        var longest = 0;

        foreach (var project in workspace.Solution.Projects)
        {
            var name = Path.GetFileNameWithoutExtension(project.FilePath.AsSpan());
            var shared = name.CommonPrefixLength(segment);

            if (shared > longest)
                (closest, longest) = (name.ToString(), shared);
        }

        return closest;
    }

    private static Project? ProjectNamed(LoadedWorkspace workspace, string path, string full) =>
        workspace.Solution.Projects.FirstOrDefault(project =>
            Path.GetFileNameWithoutExtension(project.FilePath.AsSpan()).Equals(path, StringComparison.OrdinalIgnoreCase)
            || PathBoundary.SameFile(project.FilePath, full));

    private static SearchScope ScopeOf(LoadedWorkspace workspace, string directory)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(directory);

        return new(trimmed + Path.DirectorySeparatorChar, PositionFormat.Relative(workspace.Root, trimmed));
    }
}
