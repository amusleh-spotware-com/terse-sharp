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
        && (Path.GetFullPath(first).Equals(Path.GetFullPath(second), Comparison)
            || Resolved(first).Equals(Resolved(second), Comparison));

    private static string Resolved(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Path.GetFullPath(path);
        }
    }
}
