namespace TerseSharp.Core;

public static class NearestPaths
{
    public const int MaxShown = 5;

    private static readonly EnumerationOptions Options = new() { IgnoreInaccessible = true };

    public static IReadOnlyList<string> Around(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));

        return Ancestor(root, Path.GetDirectoryName(full)) is { } directory
            ? Ranked(root, directory, Path.GetFileName(full))
            : [];
    }

    private static string? Ancestor(string root, string? directory)
    {
        for (var current = directory; current is not null && PathBoundary.Contains(root, current); current = Path.GetDirectoryName(current))
        {
            if (Directory.Exists(current))
                return current;
        }

        return null;
    }

    private static List<string> Ranked(string root, string directory, string name)
    {
        var candidates = Candidates(directory, name);

        candidates.Sort(static (left, right) => left.Shared != right.Shared
            ? right.Shared.CompareTo(left.Shared)
            : string.CompareOrdinal(left.Path, right.Path));

        var shown = new List<string>(Math.Min(MaxShown, candidates.Count));

        for (var index = 0; index < shown.Capacity; index++)
            shown.Add(PositionFormat.Relative(root, candidates[index].Path));

        return shown;
    }

    private static List<(int Shared, string Path)> Candidates(string directory, string name)
    {
        var extension = Path.GetExtension(name.AsSpan());
        var candidates = new List<(int Shared, string Path)>();

        foreach (var file in Directory.EnumerateFiles(directory, "*", Options))
        {
            if (Path.GetExtension(file.AsSpan()).Equals(extension, StringComparison.OrdinalIgnoreCase))
                candidates.Add((name.AsSpan().CommonPrefixLength(Path.GetFileName(file.AsSpan())), file));
        }

        return candidates;
    }
}
