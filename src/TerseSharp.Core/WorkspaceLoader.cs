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
        var failures = new List<string>();
        var workspace = MSBuildWorkspace.Create();

        workspace.SkipUnrecognizedProjects = true;
        workspace.RegisterWorkspaceFailedHandler(args => failures.Add(args.Diagnostic.Message));

        var stopwatch = Stopwatch.StartNew();
        var solution = await OpenAsync(workspace, full, cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        var result = Describe(full, solution, stopwatch.ElapsedMilliseconds, failures);

        return new LoadedWorkspace(workspace, result, GitContext.Detect(full), seed);
    }

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
        IReadOnlyList<string> failures) =>
        new(
            path,
            solution.Projects.Count(),
            solution.Projects.Sum(project => project.Documents.Count()),
            elapsedMilliseconds,
            Deduplicate(failures));

    private static string[] Deduplicate(IReadOnlyList<string> failures) =>
        [.. failures.Distinct(StringComparer.Ordinal).Take(20)];
}
