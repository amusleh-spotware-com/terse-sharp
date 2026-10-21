using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public readonly record struct StaleBinary(string ProjectPath, string OutputPath, string NewestInput, TimeSpan Behind);

public static class StaleBinaries
{
    public static ImmutableArray<StaleBinary> Detect(Solution solution)
    {
        var newest = new Dictionary<ProjectId, (string Path, DateTime Written)>(solution.ProjectIds.Count);

        foreach (var project in solution.Projects)
            newest[project.Id] = NewestInput(project);

        var graph = solution.GetProjectDependencyGraph();
        var stale = ImmutableArray.CreateBuilder<StaleBinary>();

        foreach (var project in solution.Projects)
        {
            if (Behind(project, Closure(project.Id, graph, newest)) is { } binary)
                stale.Add(binary);
        }

        return stale.ToImmutable();
    }

    private static (string Path, DateTime Written) Closure(
        ProjectId project,
        ProjectDependencyGraph graph,
        Dictionary<ProjectId, (string Path, DateTime Written)> newest)
    {
        var latest = newest[project];

        foreach (var dependency in graph.GetProjectsThatThisProjectTransitivelyDependsOn(project))
        {
            if (newest.TryGetValue(dependency, out var candidate) && candidate.Written > latest.Written)
                latest = candidate;
        }

        return latest;
    }

    private static StaleBinary? Behind(Project project, (string Path, DateTime Written) input)
    {
        if (project is not { FilePath: { Length: > 0 } projectPath, OutputFilePath: { Length: > 0 } output } || !File.Exists(output))
            return null;

        var built = File.GetLastWriteTimeUtc(output);

        return input.Written > built ? new StaleBinary(projectPath, output, input.Path, input.Written - built) : null;
    }

    private static (string Path, DateTime Written) NewestInput(Project project)
    {
        var latest = Written(project.FilePath);
        var intermediate = Intermediate(project.FilePath);

        foreach (var document in project.Documents)
        {
            if (document.FilePath is not { Length: > 0 } path || PathBoundary.Contains(intermediate, path))
                continue;

            if (Written(path) is var candidate && candidate.Written > latest.Written)
                latest = candidate;
        }

        return latest;
    }

    private static string Intermediate(string? projectPath) =>
        Path.Combine(Path.GetDirectoryName(projectPath) ?? string.Empty, "obj");

    private static (string Path, DateTime Written) Written(string? path) =>
        path is { Length: > 0 } && File.Exists(path) ? (path, File.GetLastWriteTimeUtc(path)) : (string.Empty, DateTime.MinValue);
}
