namespace TerseSharp.Core;

public static class PathBoundary
{
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
        try
        {
            var file = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(file);

            return directory is { Length: > 0 } parent && Directory.ResolveLinkTarget(parent, returnFinalTarget: true) is { } target
                ? Path.Combine(target.FullName, Path.GetFileName(file))
                : file;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Path.GetFullPath(path);
        }
    }

    public static StringComparer Comparer { get; } = OperatingSystem.IsLinux()
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
}
