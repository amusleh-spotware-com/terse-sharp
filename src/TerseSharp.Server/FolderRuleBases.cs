namespace TerseSharp.Server;

internal static class FolderRuleBases
{
    private static readonly EnumerationOptions OneLevelDown = new()
    {
        RecurseSubdirectories = true,
        MaxRecursionDepth = 1,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    internal static IReadOnlyList<string> For(string cwd, string? workspace)
    {
        var session = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cwd));
        var solutions = Solutions(session);
        var named = solutions.Exists(solution => Names(solution, workspace)) ? [] : Worktrees(session, workspace);

        return [.. named.Append(session).Concat(solutions.Select(solution => solution.DirectoryName).OfType<string>()).Distinct(PathBoundary.Comparer)];
    }

    private static IEnumerable<string> Worktrees(string session, string? workspace)
    {
        if (workspace is not { Length: > 0 })
            return [];

        string[] candidates = Path.IsPathFullyQualified(workspace)
            ? [workspace]
            : [Path.Join(Path.GetDirectoryName(session), workspace), Path.Join(session, ".claude", "worktrees", workspace)];

        return candidates.Select(Rooted).OfType<string>();
    }

    private static string? Rooted(string candidate) => candidate switch
    {
        _ when File.Exists(candidate) => Path.GetDirectoryName(Path.GetFullPath(candidate)),
        _ when Directory.Exists(candidate) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate)),
        _ => null,
    };

    private static List<FileInfo> Solutions(string session) =>
        Directory.Exists(session) ? [.. new DirectoryInfo(session).EnumerateFiles("*.sln*", OneLevelDown)] : [];

    private static bool Names(FileInfo solution, string? workspace) =>
        workspace is { Length: > 0 } && Path.GetFileNameWithoutExtension(solution.Name.AsSpan()).Equals(workspace, StringComparison.OrdinalIgnoreCase);
}
