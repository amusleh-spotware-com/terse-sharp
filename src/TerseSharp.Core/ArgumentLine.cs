using System.Text;

namespace TerseSharp.Core;

public static class ArgumentLine
{
    private const int MaxPaths = 10;

    public static string? Paths(IEnumerable<string> records)
    {
        var seen = new HashSet<string>(MaxPaths, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>(MaxPaths);

        foreach (var record in records)
        {
            if (ordered.Count == MaxPaths)
                break;

            if (PathOf(record) is { Length: > 0 } path && seen.Add(path))
                ordered.Add(path);
        }

        return ordered.Count < 2 ? null : Rendered("paths", ordered);
    }

    public static string? Ids(IReadOnlyList<string> ids) =>
        ids.Count is < 2 or > MaxPaths ? null : Rendered("symbolIds", ids);

    private static string Rendered(string parameter, IReadOnlyList<string> values)
    {
        var text = new StringBuilder(values.Count * 32);

        text.Append(parameter).Append("=[");

        for (var index = 0; index < values.Count; index++)
        {
            text.Append(index is 0 ? "\"" : ",\"");
            text.Append(values[index].Replace('\\', '/'));
            text.Append('"');
        }

        return text.Append(']').ToString();
    }

    private static string? PathOf(string record)
    {
        if (record.Length is 0 || char.IsWhiteSpace(record[0]))
            return null;

        var end = record.IndexOf("  ", StringComparison.Ordinal);
        var head = end < 0 ? record.AsSpan() : record.AsSpan(0, end);
        var colon = head.LastIndexOf(':');

        return colon > 0 && IsLineNumber(head[(colon + 1)..])
            ? new string(head[..colon])
            : new string(head);
    }

    private static bool IsLineNumber(ReadOnlySpan<char> text)
    {
        if (text.Length is 0)
            return false;

        foreach (var character in text)
        {
            if (!char.IsAsciiDigit(character))
                return false;
        }

        return true;
    }
}
