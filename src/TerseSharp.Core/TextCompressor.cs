using System.Collections.Frozen;
using System.Text;

namespace TerseSharp.Core;

public static class TextCompressor
{
    public static string Source(string text) => Dedented(text, CommonIndent(text));

    private static string Dedented(string text, int indent)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var line in text.AsSpan().EnumerateLines())
            builder.Append(line.TrimEnd().IsEmpty ? line : line[indent..]).Append('\n');

        return builder.ToString().TrimEnd('\n');
    }

    public static bool KeepsBlankLines(ReadOnlySpan<char> path) =>
        !Extensions.Contains(Path.GetExtension(path));

    private static int CommonIndent(string text)
    {
        var indent = int.MaxValue;

        foreach (var line in text.AsSpan().EnumerateLines())
        {
            var trimmed = line.TrimEnd();

            if (!trimmed.IsEmpty)
                indent = Math.Min(indent, trimmed.Length - trimmed.TrimStart().Length);
        }

        return indent is int.MaxValue ? 0 : indent;
    }

    private static readonly FrozenSet<string> Insignificant = new[]
    {
        ".cs", ".razor", ".cshtml", ".xaml", ".axaml", ".xml", ".xsd", ".resx", ".config",
        ".csproj", ".vbproj", ".fsproj", ".props", ".targets", ".slnx", ".json", ".css",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> Extensions =
        Insignificant.GetAlternateLookup<ReadOnlySpan<char>>();

    public static bool HasMultilineLiteral(string text) =>
        text.Contains("\"\"\"", StringComparison.Ordinal) || text.Contains("@\"", StringComparison.Ordinal);
}
