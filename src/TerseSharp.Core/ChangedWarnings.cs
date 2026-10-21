namespace TerseSharp.Core;

public static class ChangedWarnings
{
    public static HashSet<string> Paths(string root, string trackedNames, string untrackedNames)
    {
        var paths = new HashSet<string>(StringComparer.FromComparison(PathBoundary.Comparison));

        Add(paths, root, trackedNames);
        Add(paths, root, untrackedNames);

        return paths;
    }

    public static int Count(IReadOnlyList<string> warnings, IReadOnlySet<string> changed)
    {
        var count = 0;

        foreach (var warning in warnings)
            count += FileOf(warning) is { Length: > 0 } file && changed.Contains(file) ? 1 : 0;

        return count;
    }

    public static string FileOf(string warning)
    {
        var line = warning.AsSpan().TrimStart();
        var marker = line.IndexOf(": warning ", StringComparison.Ordinal);

        if (marker < 0)
            return string.Empty;

        var head = line[..marker].TrimEnd();
        var position = head.LastIndexOf('(');

        return (position > 0 && head[^1] is ')' ? head[..position] : head).ToString();
    }

    private static void Add(HashSet<string> paths, string root, string names)
    {
        foreach (var line in names.AsSpan().EnumerateLines())
        {
            var name = line.Trim();

            if (name.Length > 0)
                paths.Add(Path.GetFullPath(Path.Join(root.AsSpan(), name)));
        }
    }
}
