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

        while (true)
        {
            var next = relative.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (IsOutput(next < 0 ? relative : relative[..next]))
                return true;

            if (next < 0)
                return false;

            relative = relative[(next + 1)..];
        }
    }

    private static bool IsOutput(ReadOnlySpan<char> segment) =>
        segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("bin", StringComparison.OrdinalIgnoreCase);
}
