using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public readonly record struct TestSelection(
    ImmutableArray<string> Run,
    ImmutableArray<string> Skipped,
    string? FullRunReason)
{
    public bool IsFullRun => FullRunReason is not null;

    public static TestSelection Full(string reason) => new([], [], reason);
}

public static class ChangedTestSelection
{
    public static TestSelection Select(LoadedWorkspace workspace) =>
        Select(workspace.Solution, DocumentScope.Select(workspace, null, changedOnly: true));

    public static TestSelection Select(Solution solution, DocumentId[] changed)
    {
        if (changed.Length is 0)
            return TestSelection.Full("no document has changed since the workspace loaded");

        var origins = Origins(solution, changed);

        if (origins.Count is 0)
            return TestSelection.Full("a changed file belongs to no project in this solution");

        var affected = Affected(solution, origins);
        var run = Paths(solution, affected);
        var skipped = Paths(solution, TestProjects(solution).Where(id => !affected.Contains(id)));

        return run.Length is 0
            ? TestSelection.Full("no test project depends on the changed projects")
            : new TestSelection(run, skipped, null);
    }

    private static HashSet<ProjectId> Origins(Solution solution, DocumentId[] changed)
    {
        var origins = new HashSet<ProjectId>();

        foreach (var id in changed)
        {
            if (solution.GetDocument(id) is { } document)
                origins.Add(document.Project.Id);
        }

        return origins;
    }

    private static HashSet<ProjectId> Affected(Solution solution, HashSet<ProjectId> origins)
    {
        var graph = solution.GetProjectDependencyGraph();
        var affected = new HashSet<ProjectId>();

        foreach (var origin in origins)
        {
            Keep(solution, affected, origin);

            foreach (var dependent in graph.GetProjectsThatTransitivelyDependOnThisProject(origin))
                Keep(solution, affected, dependent);
        }

        return affected;
    }

    private static void Keep(Solution solution, HashSet<ProjectId> affected, ProjectId id)
    {
        if (solution.GetProject(id) is { } project && TestScope.Of(project) is "test")
            affected.Add(id);
    }

    private static IEnumerable<ProjectId> TestProjects(Solution solution) => solution.Projects
        .Where(project => TestScope.Of(project) is "test")
        .Select(project => project.Id);

    private static ImmutableArray<string> Paths(Solution solution, IEnumerable<ProjectId> projects) =>
    [
        .. projects
            .Select(id => solution.GetProject(id)?.FilePath)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase),
    ];
}
