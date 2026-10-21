using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public readonly record struct StaleBinary(string ProjectPath, string OutputPath, string NewestInput, TimeSpan Behind);

public static class StaleBinaries
{
    public static ImmutableArray<StaleBinary> Detect(Solution solution)
    {
        var own = new Dictionary<ProjectId, StaleBinary?>(solution.ProjectIds.Count);

        foreach (var project in solution.Projects)
            own[project.Id] = Behind(project, NewestInput(project));

        var graph = solution.GetProjectDependencyGraph();
        var stale = ImmutableArray.CreateBuilder<StaleBinary>();

        foreach (var project in solution.Projects)
        {
            if (Staleness(solution, project, graph, own) is { } binary)
                stale.Add(binary);
        }

        return stale.ToImmutable();
    }

    private static StaleBinary? Staleness(
        Solution solution,
        Project project,
        ProjectDependencyGraph graph,
        Dictionary<ProjectId, StaleBinary?> own)
    {
        if (own[project.Id] is { } self)
            return self;

        foreach (var dependency in graph.GetProjectsThatThisProjectTransitivelyDependsOn(project.Id))
        {
            if (own.GetValueOrDefault(dependency) is { } unbuilt && project.FilePath is { Length: > 0 } projectPath)
                return unbuilt with { ProjectPath = projectPath };

            if (solution.GetProject(dependency) is { } library && Uncopied(project, library) is { } uncopied)
                return uncopied;
        }

        return null;
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

    private static StaleBinary? Uncopied(Project project, Project library)
    {
        if (project is not { FilePath: { Length: > 0 } projectPath, OutputFilePath: { Length: > 0 } output } || library.OutputFilePath is not { Length: > 0 } built || !File.Exists(built))
            return null;

        var copy = Path.Join(Path.GetDirectoryName(output), Path.GetFileName(built));

        if (!File.Exists(copy))
            return null;

        var copied = File.GetLastWriteTimeUtc(copy);
        var fresh = File.GetLastWriteTimeUtc(built);

        return fresh > copied ? new StaleBinary(projectPath, copy, built, fresh - copied) : null;
    }
}
