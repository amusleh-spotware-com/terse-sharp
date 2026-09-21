using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace TerseSharp.Core;

internal static class WorkspaceLoader
{
    public static async Task<LoadedWorkspace> LoadAsync(string path, WorkspaceSeed seed, CancellationToken cancellationToken)
    {
        MsBuildBootstrap.Ensure();

        var full = Path.GetFullPath(path);
        var reported = new ConcurrentQueue<WorkspaceDiagnostic>();
        var workspace = Created(seed.TargetFramework);

        workspace.SkipUnrecognizedProjects = true;
        workspace.RegisterWorkspaceFailedHandler(args => reported.Enqueue(args.Diagnostic));

        var stopwatch = Stopwatch.StartNew();
        var solution = await OpenAsync(workspace, full, cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        var result = Describe(full, solution, stopwatch.ElapsedMilliseconds, reported, seed.TargetFramework);

        return new LoadedWorkspace(workspace, result, GitContext.Detect(full), seed);
    }

    private static MSBuildWorkspace Created(string? targetFramework) => targetFramework is { Length: > 0 } framework
        ? MSBuildWorkspace.Create(new Dictionary<string, string>(StringComparer.Ordinal) { ["TargetFramework"] = framework })
        : MSBuildWorkspace.Create();

    private static async Task<Solution> OpenAsync(
        MSBuildWorkspace workspace,
        string path,
        CancellationToken cancellationToken)
    {
        if (WorkspaceDiscovery.IsSolution(path))
            return await workspace.OpenSolutionAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false);

        var project = await workspace.OpenProjectAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return project.Solution;
    }

    private static WorkspaceLoadResult Describe(
        string path,
        Solution solution,
        long elapsedMilliseconds,
        IReadOnlyCollection<WorkspaceDiagnostic> reported,
        string? targetFramework)
    {
        var loaded = Loaded(solution).GetAlternateLookup<ReadOnlySpan<char>>();
        var stopped = Messages(reported, loaded, stopped: true);
        var empty = EmptyProjectLoad.Failures(solution);

        return new(
            path,
            solution.Projects.Count(),
            solution.Projects.Sum(project => project.Documents.Count()),
            elapsedMilliseconds,
            empty.Length is 0 ? stopped : [.. stopped, .. empty],
            Messages(reported, loaded, stopped: false),
            targetFramework,
            AnalyzerRebind.Unresolved(solution));
    }

    private static string[] Messages(
        IReadOnlyCollection<WorkspaceDiagnostic> reported,
        HashSet<string>.AlternateLookup<ReadOnlySpan<char>> loaded,
        bool stopped) =>
        [.. reported
            .Where(diagnostic => StoppedALoad(diagnostic, loaded) == stopped)
            .Select(diagnostic => diagnostic.Message)
            .Distinct(StringComparer.Ordinal)
            .Take(20)];

    private static HashSet<string> Loaded(Solution solution)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.Projects)
        {
            if (project.FilePath is { Length: > 0 } path)
                paths.Add(path);
        }

        return paths;
    }

    private static bool StoppedALoad(WorkspaceDiagnostic diagnostic, HashSet<string>.AlternateLookup<ReadOnlySpan<char>> loaded) =>
        diagnostic.Kind is WorkspaceDiagnosticKind.Failure
        && !loaded.Contains(LoadFailureSummary.ProjectPathOf(diagnostic.Message));
}
