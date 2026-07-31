namespace TerseSharp.Core;

public static class GeneratedCode
{
    private static readonly string[] OutputDirectories = ["obj", "bin"];

    private static readonly string[] Suffixes =
        [".g.cs", ".g.i.cs", ".generated.cs", ".designer.cs", "AssemblyInfo.cs", "AssemblyAttributes.cs"];

    public static bool IsGenerated(string root, string? path) =>
        path is { Length: > 0 } file && (HasGeneratedSuffix(file) || InOutputDirectory(root, file));

    private static bool HasGeneratedSuffix(string file) =>
        Suffixes.Any(suffix => file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    private static bool InOutputDirectory(string root, string file) =>
        PathBoundary.Contains(root, file)
        && Path.GetRelativePath(root, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => OutputDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));
}
