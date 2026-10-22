using System.Buffers;

namespace TerseSharp.Server;

internal static class FolderRulePaths
{
    internal const string FileName = "CLAUDE.md";

    private const string GitMarker = ".git";

    private static readonly SearchValues<char> Wildcards = SearchValues.Create("*?{");

    internal static IReadOnlyList<string> Rules(IEnumerable<string> targets, IEnumerable<string> mentioned, IReadOnlyList<string> bases, string cwd) =>
    [
        .. targets.Select(path => Folder(path, bases))
            .Concat(mentioned.Select(path => Folder(path, bases)).Where(folder => folder is not null && bases.Any(root => PathBoundary.Contains(root, folder))))
            .OfType<string>()
            .Distinct(PathBoundary.Comparer)
            .SelectMany(folder => Nested(folder, cwd))
            .Distinct(PathBoundary.Comparer),
    ];

    private static string? Folder(string mentioned, IReadOnlyList<string> bases)
    {
        var trimmed = mentioned.AsSpan().Trim().Trim("\"'");

        if (trimmed.IsEmpty || trimmed.ContainsAny(Wildcards))
            return null;

        var resolved = Path.TrimEndingDirectorySeparator(Resolved(Hosted(trimmed), bases));

        return Directory.Exists(resolved) ? resolved : Path.GetDirectoryName(resolved);
    }

    private static string Hosted(ReadOnlySpan<char> path)
    {
        var text = path.ToString();

        return OperatingSystem.IsWindows() ? text : text.Replace('\\', '/');
    }

    private static string Resolved(string path, IReadOnlyList<string> bases)
    {
        if (Path.IsPathFullyQualified(path))
            return Path.GetFullPath(path);

        var candidates = bases.Select(root => Path.GetFullPath(Path.Join(root, path))).ToList();

        return candidates.Find(candidate => File.Exists(candidate) || Directory.Exists(Path.GetDirectoryName(candidate))) ?? candidates[0];
    }

    private static List<string> Nested(string folder, string cwd)
    {
        var boundary = PathBoundary.Contains(cwd, folder) ? Path.GetFullPath(cwd) : RepositoryRoot(folder);
        var found = new List<string>();

        for (var current = folder; boundary is not null && current is not null && StrictlyBelow(boundary, current); current = Path.GetDirectoryName(current))
        {
            if (!PathBoundary.Contains(current, cwd) && File.Exists(Path.Join(current, FileName)))
                found.Add(Path.Join(current, FileName));
        }

        found.Reverse();

        return found;
    }

    private static bool StrictlyBelow(string boundary, string current) =>
        PathBoundary.Contains(boundary, current) && !PathBoundary.Contains(current, boundary);

    private static string? RepositoryRoot(string folder)
    {
        for (var current = folder; current is not null; current = Path.GetDirectoryName(current))
        {
            var marker = Path.Join(current, GitMarker);

            if (Directory.Exists(marker) || File.Exists(marker))
                return current;
        }

        return null;
    }

    internal static IReadOnlyList<string> RuleFiles(IEnumerable<string> targets, IReadOnlyList<string> bases) =>
        [.. targets.Select(path => RuleFile(path, bases)).OfType<string>()];

    private static string? RuleFile(string named, IReadOnlyList<string> bases)
    {
        var hosted = Hosted(named.AsSpan().Trim().Trim("\"'"));

        if (!Path.GetFileName(hosted.AsSpan()).Equals(FileName, StringComparison.OrdinalIgnoreCase))
            return null;

        var resolved = Resolved(hosted, bases);

        return File.Exists(resolved) ? resolved : null;
    }
}
