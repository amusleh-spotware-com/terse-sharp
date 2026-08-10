using System.Buffers;
using System.Collections.Immutable;

namespace TerseSharp.Core;

public readonly record struct WorkspaceTarget(
    string SolutionPath,
    string Root,
    ImmutableArray<string> ProjectPaths = default,
    TestSelection Tests = default)
{
    private const int MaxSuggestions = 8;

    private static readonly string[] ProjectExtensions = [".csproj", ".vbproj", ".fsproj"];

    private static readonly SearchValues<char> NotInAName = SearchValues.Create("/\\*?");

    public Result<string> ResolveProject(string project)
    {
        var resolved = PathGuard.Resolve(Root, project);

        return resolved.IsOk && !Exists(resolved.Value!) ? Named(project) : resolved;
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private Result<string> Named(string project)
    {
        if (project.AsSpan().IndexOfAny(NotInAName) >= 0)
            return Result.Fail<string>(Errors.ProjectNotFound(project, Closest(project)));

        var name = Bare(project);

        return Matching(name) switch
        {
            [var only] => Result.Ok(only),
            [] => OnDisk(name, project),
            var several => Result.Fail<string>(Errors.AmbiguousProject(project, several)),
        };
    }

    private static string Bare(string project) =>
        HasProjectExtension(project) ? Path.GetFileNameWithoutExtension(project) : project;

    private static bool HasProjectExtension(string project)
    {
        foreach (var extension in ProjectExtensions)
        {
            if (project.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private Result<string> OnDisk(string name, string project) => Search(name) switch
    {
        [var only] => Result.Ok(only),
        [] => Result.Fail<string>(Errors.ProjectNotFound(project, Closest(name))),
        var several => Result.Fail<string>(Errors.AmbiguousProject(project, several)),
    };

    private string[] Search(string project) => Directory.Exists(Root)
        ? [.. WorkspaceFiles.Enumerate(Root, path => IsNamed(path, project))]
        : [];

    private string[] Matching(string project)
    {
        if (ProjectPaths.IsDefaultOrEmpty)
            return [];

        var matches = new List<string>(1);

        foreach (var path in ProjectPaths)
        {
            if (Path.GetFileNameWithoutExtension(path.AsSpan()).Equals(project, StringComparison.OrdinalIgnoreCase))
                matches.Add(path);
        }

        return [.. matches];
    }

    private string[] Closest(string project)
    {
        if (ProjectPaths.IsDefaultOrEmpty)
            return [];

        var closest = new List<string>(MaxSuggestions);

        foreach (var path in ProjectPaths)
        {
            if (closest.Count is MaxSuggestions)
                break;

            if (Overlaps(Path.GetFileNameWithoutExtension(path.AsSpan()), project))
                closest.Add(Path.GetFileNameWithoutExtension(path));
        }

        return [.. closest];
    }

    private static bool Overlaps(ReadOnlySpan<char> name, string project) =>
        name.Contains(project, StringComparison.OrdinalIgnoreCase)
        || project.AsSpan().Contains(name, StringComparison.OrdinalIgnoreCase);

    private static bool IsNamed(string path, string project) =>
            WorkspaceFiles.Matches(Path.GetExtension(path.AsSpan()), ProjectExtensions)
            && Path.GetFileNameWithoutExtension(path.AsSpan()).Equals(project, StringComparison.OrdinalIgnoreCase);
}
