using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

public static class PositionFormat
{
    public static string Describe(Location location)
    {
        var span = Source(location);

        return Describe(span.Path, span.StartLinePosition);
    }

    public static string Describe(string root, Location location)
    {
        var span = Source(location);

        return Describe(Relative(root, span.Path), span.StartLinePosition);
    }

    public static string Describe(string path, LinePosition position) =>
        string.Create(CultureInfo.InvariantCulture, $"{path}:{position.Line + 1}:{position.Character + 1}");

    public static string Relative(string root, string? path) =>
        path is not { Length: > 0 } file
            ? "-"
            : PathBoundary.Contains(root, file) ? Path.GetRelativePath(root, file) : file;

    public static FileLinePositionSpan Source(Location location)
    {
        var mapped = location.GetMappedLineSpan();

        return mapped.HasMappedPath ? mapped : location.GetLineSpan();
    }

    public static string Range(Location location)
    {
        var span = Source(location);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{span.Path}:{span.StartLinePosition.Line + 1}-{span.EndLinePosition.Line + 1}");
    }

    public static string LineRange(SyntaxNode node)
    {
        var span = Source(node.GetLocation());

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{span.StartLinePosition.Line + 1}-{span.EndLinePosition.Line + 1}");
    }
}
