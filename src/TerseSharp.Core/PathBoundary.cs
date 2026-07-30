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
}
