namespace TerseSharp.Server;

public static class StaleRun
{
    private const int MaxNamed = 3;

    public static string Annotated(string verdict, int before, int after, IReadOnlyList<string>? roots = null) =>
        Note(before, after, roots) is { } note ? verdict + "\n" + note : verdict;

    public static string? Note(int before, int after, IReadOnlyList<string>? roots = null) => after > before
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"STALE {after - before} document(s) changed after this run started{Named(before, roots)} - this verdict describes the tree as it was when the run began, not as it is now; re-run it")
        : null;

    private static string Named(int before, IReadOnlyList<string>? roots)
    {
        var paths = EditPulse.Since(before, MaxNamed);

        return paths.Count is 0
            ? string.Empty
            : " (" + string.Join(", ", paths.Select(path => Relative(path, roots))) + ")";
    }

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
