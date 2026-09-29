namespace TerseSharp.Core;

public static class PathBoundary
{
    private const int MaxLinkHops = 16;

    public static StringComparison Comparison { get; } = OperatingSystem.IsLinux()
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;

    public static bool Contains(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));

        return full.Equals(normalizedRoot, Comparison)
            || full.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, Comparison);
    }

    public static bool SameFile(string? left, string? right) =>
        left is { Length: > 0 } first
        && right is { Length: > 0 } second
        && Path.GetFullPath(first).Equals(Path.GetFullPath(second), Comparison);

    public static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);

        try
        {
            return Resolved(full, MaxLinkHops);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return full;
        }
    }

    private static string Resolved(string full, int hops)
    {
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var current = root;

        foreach (var range in full.AsSpan(root.Length).SplitAny(Separators))
        {
            var part = full.AsSpan(root.Length)[range];

            if (!part.IsEmpty)
                current = Linked(Path.Join(current, part), hops);
        }

        return current;
    }

    private static string Linked(string path, int hops) =>
        hops > 0 && new DirectoryInfo(path) is { LinkTarget: not null } link && link.ResolveLinkTarget(returnFinalTarget: true) is { } target
            ? Resolved(target.FullName, hops - 1)
            : path;

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public static StringComparer Comparer { get; } = OperatingSystem.IsLinux()
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
}
