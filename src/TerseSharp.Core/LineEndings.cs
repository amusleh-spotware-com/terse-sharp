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
}
