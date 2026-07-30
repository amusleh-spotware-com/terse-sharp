using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

public static class PositionFormat
{
    public static string Describe(Location location)
    {
        var span = location.GetLineSpan();

        return Describe(span.Path, span.StartLinePosition);
    }

    public static string Describe(string path, LinePosition position) =>
        string.Create(CultureInfo.InvariantCulture, $"{path}:{position.Line + 1}:{position.Character + 1}");

    public static string Range(Location location)
    {
        var span = location.GetLineSpan();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{span.Path}:{span.StartLinePosition.Line + 1}-{span.EndLinePosition.Line + 1}");
    }

    public static string LineRange(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{span.StartLinePosition.Line + 1}-{span.EndLinePosition.Line + 1}");
    }
}
