namespace TerseSharp.Core;

public static class GeneratedCode
{

    private static readonly string[] Suffixes =
        [".g.cs", ".g.i.cs", ".generated.cs", ".designer.cs", "AssemblyInfo.cs", "AssemblyAttributes.cs"];

    public static bool IsGenerated(string root, string? path) =>
        path is { Length: > 0 } file && (HasGeneratedSuffix(file) || InOutputDirectory(root, file));

    private static bool HasGeneratedSuffix(string file) =>
        Suffixes.Any(suffix => file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    private static bool InOutputDirectory(string root, string file)
    {
        if (!PathBoundary.Contains(root, file))
            return false;

        var relative = Path.GetRelativePath(root, file).AsSpan();

        while (relative.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) is var next and >= 0)
        {
            if (IsOutput(relative[..next]))
                return true;

            relative = relative[(next + 1)..];
        }

        return false;
    }

    private static bool IsOutput(ReadOnlySpan<char> segment) =>
        segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("bin", StringComparison.OrdinalIgnoreCase);
}
