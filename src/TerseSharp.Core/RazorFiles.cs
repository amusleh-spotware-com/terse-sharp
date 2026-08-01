namespace TerseSharp.Core;

public sealed record RazorAssets(string? CodeBehind, string? ScopedCss, string? JavaScript, IReadOnlyList<string> Imports);

public static class RazorFiles
{
    private static readonly string[] GeneratedSuffixes = ["_razor.g.cs", "_cshtml.g.cs"];

    public static IEnumerable<string> Enumerate(string root) =>
        WorkspaceFiles.Enumerate(root, RazorDocument.IsRazor).Where(file => !IsGenerated(file));

    public static bool IsGenerated(ReadOnlySpan<char> path)
    {
        foreach (var suffix in GeneratedSuffixes)
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static RazorAssets AssetsOf(string razorPath)
    {
        var directory = Path.GetDirectoryName(razorPath) ?? string.Empty;
        var name = Path.GetFileName(razorPath);

        return new RazorAssets(
            Sibling(directory, name + ".cs"),
            Sibling(directory, name + ".css"),
            Sibling(directory, name + ".js"),
            [.. ImportsFor(razorPath)]);
    }

    public static IEnumerable<string> ImportsFor(string razorPath)
    {
        var imports = Path.GetExtension(razorPath).Equals(".cshtml", StringComparison.OrdinalIgnoreCase)
            ? "_ViewImports.cshtml"
            : "_Imports.razor";

        return Ancestors(Path.GetDirectoryName(razorPath))
            .Select(directory => Path.Combine(directory, imports))
            .Where(File.Exists)
            .Reverse();
    }

    private static IEnumerable<string> Ancestors(string? directory)
    {
        for (var current = directory; current is { Length: > 0 }; current = Path.GetDirectoryName(current))
            yield return current;
    }

    private static string? Sibling(string directory, string name)
    {
        var candidate = Path.Combine(directory, name);

        return File.Exists(candidate) ? candidate : null;
    }
}
