namespace TerseSharp.Server;

public static class StaleRun
{
    private const int MaxNamed = 3;

    public static string Annotated(string verdict, int before, int after, IReadOnlyList<string>? roots = null) =>
        Note(before, after, roots) is { } note ? verdict + "\n" + note : verdict;

    public static string? Note(int before, int after, IReadOnlyList<string>? roots = null)
    {
        var written = EditPulse.Written(before, after);
        var stale = after - before - Foreign(written, roots);

        return stale > 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"STALE {stale} document(s) changed after this run started{Named(written, roots)} - this verdict describes the tree as it was when the run began, not as it is now; re-run it")
            : null;
    }

    private static int Foreign(IReadOnlyList<string> written, IReadOnlyList<string>? roots) =>
        roots is { Count: > 0 } ? written.Count(path => !Inside(path, roots)) : 0;

    private static string Named(IReadOnlyList<string> written, IReadOnlyList<string>? roots)
    {
        var paths = written
            .Where(path => roots is not { Count: > 0 } || Inside(path, roots))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxNamed)
            .Select(path => Relative(path, roots))
            .ToList();

        return paths.Count is 0 ? string.Empty : " (" + string.Join(", ", paths) + ")";
    }

    private static bool Inside(string path, IReadOnlyList<string> roots) =>
        roots.Any(root => PathBoundary.Contains(root, path) && !WorkspaceFiles.IsExcluded(path, root));

    private static string Relative(string path, IReadOnlyList<string>? roots)
    {
        foreach (var root in roots ?? [])
        {
            if (PathBoundary.Contains(root, path))
                return PositionFormat.Relative(root, path);
        }

        return path;
    }
}
