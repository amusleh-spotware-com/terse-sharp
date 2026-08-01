using System.Text;

namespace TerseSharp.Core;

public static class LineEndings
{
    public const string Windows = "\r\n";

    public const string Unix = "\n";

    public static string Dominant(ReadOnlySpan<char> text)
    {
        var (windows, unix) = Count(text);

        return unix > windows ? Unix : Windows;
    }

    public static string Normalize(string text) =>
        text.Contains('\r', StringComparison.Ordinal) ? text.ReplaceLineEndings(Unix) : text;

    public static string Adopt(string content, string ending) =>
        content.ReplaceLineEndings(ending);

    public static int OriginalOffset(ReadOnlySpan<char> original, int normalizedOffset)
    {
        var normalized = 0;
        var index = 0;

        while (index < original.Length && normalized < normalizedOffset)
        {
            if (original[index] is not '\r' || index + 1 >= original.Length || original[index + 1] is not '\n')
                normalized++;

            index++;
        }

        return index;
    }

    private static (int Windows, int Unix) Count(ReadOnlySpan<char> text)
    {
        var windows = 0;
        var unix = 0;
        var start = 0;

        while (start < text.Length && text[start..].IndexOf('\n') is var offset and >= 0)
        {
            var index = start + offset;

            if (index > 0 && text[index - 1] is '\r')
                windows++;
            else
                unix++;

            start = index + 1;
        }

        return (windows, unix);
    }

    public static string? Uniform(ReadOnlySpan<char> text)
    {
        var (windows, unix) = Count(text);

        return (windows, unix) switch
        {
            ( > 0, 0) => Windows,
            (0, > 0) => Unix,
            _ => null,
        };
    }

    public static string Apply(string content, string ending)
    {
        var span = content.AsSpan();
        var builder = new StringBuilder(content.Length + 16);
        var index = 0;

        while (index < span.Length)
        {
            var next = span[index..].IndexOf('\n');

            if (next < 0)
                break;

            var at = index + next;
            var start = at > 0 && span[at - 1] is '\r' ? at - 1 : at;

            builder.Append(span[index..start]).Append(ending);
            index = at + 1;
        }

        return builder.Append(span[index..]).ToString();
    }
}
