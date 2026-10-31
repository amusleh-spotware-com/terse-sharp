using System.Collections.Concurrent;
using System.Text.Json;
using System.Xml;

namespace TerseSharp.Core;

public sealed record SiblingSolution(string SolutionPath, IReadOnlyList<string> MissingProjects);

public sealed record SiblingOwner(string Project, string SolutionPath);

public static class SiblingSolutions
{
    private const int NamesShown = 3;

    private static readonly ConcurrentDictionary<string, ListedProjects> Listed = new(PathBoundary.Comparer);

    public static async Task<IReadOnlyList<SiblingSolution>> WiderAsync(LoadedWorkspace workspace, CancellationToken cancellationToken)
    {
        var loaded = workspace.Solution.Projects.Select(project => project.FilePath).OfType<string>().ToHashSet(PathBoundary.Comparer);
        var wider = new List<SiblingSolution>();

        foreach (var candidate in Candidates(workspace.SolutionPath))
        {
            var missing = (await ProjectsAsync(candidate, cancellationToken).ConfigureAwait(false)).Where(project => !loaded.Contains(project)).ToArray();

            if (missing.Length > 0)
                wider.Add(new SiblingSolution(candidate, missing));
        }

        return wider;
    }

    public static async Task<SiblingOwner?> OwnerAsync(LoadedWorkspace workspace, string fullPath, CancellationToken cancellationToken)
    {
        var depth = Deepest(workspace.Solution.Projects.Select(project => project.FilePath).OfType<string>(), fullPath)?.Depth ?? -1;
        SiblingOwner? owner = null;

        foreach (var sibling in await WiderAsync(workspace, cancellationToken).ConfigureAwait(false))
        {
            if (Deepest(sibling.MissingProjects, fullPath) is { } found && found.Depth > depth)
            {
                owner = new SiblingOwner(Path.GetFileNameWithoutExtension(found.Project), sibling.SolutionPath);
                depth = found.Depth;
            }
        }

        return owner;
    }

    private static (string Project, int Depth)? Deepest(IEnumerable<string> projects, string fullPath) =>
        projects
            .Where(project => PathBoundary.Contains(Path.GetDirectoryName(project) ?? string.Empty, fullPath))
            .Select(project => ((string Project, int Depth)?)(project, Path.GetDirectoryName(project.AsSpan()).Length))
            .MaxBy(found => found?.Depth ?? -1);

    public static string? Describe(IReadOnlyList<SiblingSolution> wider, string loadedPath)
    {
        if (wider.Count is 0)
            return null;

        var named = wider.Select(sibling => string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(sibling.SolutionPath)} beside it holds {sibling.MissingProjects.Count} project(s) {Path.GetFileName(loadedPath)} does not load ({Names(sibling.MissingProjects)})"));

        return string.Join("; ", named);
    }

    public static async Task<string?> WarningAsync(LoadedWorkspace workspace, CancellationToken cancellationToken) =>
        Describe(await WiderAsync(workspace, cancellationToken).ConfigureAwait(false), workspace.SolutionPath) is { } subset
            ? "WARNING subset - " + subset + " - find_usages and find_implementations cannot see those projects; load_workspace path=<that solution> replace=true serves them"
            : null;

    public static async Task<string> NotedAsync(LoadedWorkspace workspace, Task<string> answer, CancellationToken cancellationToken)
    {
        var text = await answer.ConfigureAwait(false);

        if (text.StartsWith("ERROR", StringComparison.Ordinal))
            return text;

        return Describe(await WiderAsync(workspace, cancellationToken).ConfigureAwait(false), workspace.SolutionPath) is { } subset
            ? text + "\nNOTE incomplete - " + subset + "; references from those projects are not counted"
            : text;
    }

    private static string Names(IReadOnlyList<string> projects)
    {
        var shown = string.Join(", ", projects.Take(NamesShown).Select(project => Path.GetFileNameWithoutExtension(project)));

        return projects.Count > NamesShown
            ? string.Create(CultureInfo.InvariantCulture, $"{shown}, +{projects.Count - NamesShown} more")
            : shown;
    }

    private static IEnumerable<string> Candidates(string solutionPath)
    {
        if (!SolutionFile.IsSolutionFile(solutionPath) || Path.GetDirectoryName(solutionPath) is not { } directory || !Directory.Exists(directory))
            return [];

        return Directory.EnumerateFiles(directory, "*.sln*")
            .Where(path => SolutionFile.IsSolutionFile(path) && !string.Equals(path, solutionPath, PathBoundary.Comparison))
            .Order(PathBoundary.Comparer);
    }

    private static async Task<string[]> ProjectsAsync(string solutionPath, CancellationToken cancellationToken)
    {
        var stamp = File.GetLastWriteTimeUtc(solutionPath);

        if (Listed.TryGetValue(solutionPath, out var cached) && cached.Stamp == stamp)
            return cached.Projects;

        var projects = await ReadAsync(solutionPath, cancellationToken).ConfigureAwait(false);

        Listed[solutionPath] = new ListedProjects(stamp, projects);

        return projects;
    }

    private static async Task<string[]> ReadAsync(string solutionPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(solutionPath) ?? string.Empty;

        try
        {
            var listed = await SolutionFile.ProjectsAsync(solutionPath, cancellationToken).ConfigureAwait(false);

            return [.. listed.Select(project => Path.GetFullPath(Path.Combine(directory, SolutionFile.Slashed(project))))];
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or JsonException or XmlException)
        {
            return [];
        }
    }

    private readonly record struct ListedProjects(DateTime Stamp, string[] Projects);
}
