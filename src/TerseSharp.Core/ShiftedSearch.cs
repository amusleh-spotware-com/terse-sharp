namespace TerseSharp.Core;

internal static class ShiftedSearch
{
    public static SnippetMatch Find(string haystack, string needle, int occurrence)
    {
        var text = LineEndings.Normalize(haystack);
        var found = Locate(text, LineEndings.Normalize(needle), occurrence);

        return found.Start < 0 || ReferenceEquals(text, haystack)
            ? found
            : SnippetSearch.Mapped(haystack, found) with { Indent = found.Indent, Outdent = found.Outdent, MidLine = true, Shifted = true };
    }

    private static SnippetMatch Locate(ReadOnlySpan<char> text, ReadOnlySpan<char> value, int occurrence)
    {
        var body = value.EndsWith("\n", StringComparison.Ordinal) ? value[..^1] : value;
        var multiLine = body.Contains('\n');
        var (chosen, occurrences, start) = (ShiftedRegion.None, 0, 0);

        while (multiLine && start < text.Length)
        {
            if (Anchored(text, start, body, body.Length < value.Length) is { Start: >= 0 } found && ++occurrences == occurrence)
                chosen = found;

            start = text[start..].IndexOf('\n') is var offset and >= 0 ? start + offset + 1 : text.Length;
        }

        return chosen.Start < 0 ? new SnippetMatch(-1, value.Length, occurrences, false) : Shift(text, chosen, occurrences);
    }

    private static ShiftedRegion Anchored(ReadOnlySpan<char> text, int start, ReadOnlySpan<char> body, bool trailing)
    {
        var split = body.IndexOf('\n');
        var head = body[..split].Trim();
        var headEnd = SnippetSearch.LineEnd(text, start);
        var line = text[start..headEnd].TrimEnd();

        if (head.IsEmpty || !line.EndsWith(head, StringComparison.Ordinal) || !line[..^head.Length].IsWhiteSpace())
            return ShiftedRegion.None;

        var (end, delta, indentAt) = Rest(text, headEnd, body[(split + 1)..]);

        return end < 0 || delta is int.MinValue
            ? ShiftedRegion.None
            : new ShiftedRegion(start + line.Length - head.Length, SnippetSearch.Closed(text, end, trailing), delta, indentAt);
    }

    private static (int End, int Delta, int IndentAt) Rest(ReadOnlySpan<char> text, int end, ReadOnlySpan<char> rest)
    {
        var (delta, indentAt) = (int.MinValue, -1);

        foreach (var needle in rest.EnumerateLines())
        {
            if (end >= text.Length || ShiftOf(text[(end + 1)..SnippetSearch.LineEnd(text, end + 1)], needle) is not { } shift || Conflicts(shift, delta))
                return (-1, delta, indentAt);

            (delta, indentAt) = delta is int.MinValue && shift is not int.MinValue ? (shift, end + 1) : (delta, indentAt);
            end = SnippetSearch.LineEnd(text, end + 1);
        }

        return (end, delta, indentAt);
    }

    private static bool Conflicts(int shift, int delta) => shift is not int.MinValue && delta is not int.MinValue && shift != delta;

    private static int? ShiftOf(ReadOnlySpan<char> line, ReadOnlySpan<char> needle)
    {
        if (needle.IsWhiteSpace())
            return line.IsWhiteSpace() ? int.MinValue : null;

        var have = line[..SnippetSearch.Depth(line)];
        var want = needle[..SnippetSearch.Depth(needle)];

        return line.Trim().Equals(needle.Trim(), StringComparison.Ordinal) && Nested(have, want) ? have.Length - want.Length : null;
    }

    private static bool Nested(ReadOnlySpan<char> have, ReadOnlySpan<char> want) =>
        have.EndsWith(want, StringComparison.Ordinal) || want.EndsWith(have, StringComparison.Ordinal);

    private static SnippetMatch Shift(ReadOnlySpan<char> text, ShiftedRegion region, int occurrences) =>
        new(region.Start, region.End - region.Start, occurrences, false)
        {
            Indent = region.Delta > 0 ? text.Slice(region.IndentAt, region.Delta).ToString() : null,
            Outdent = region.Delta < 0 ? -region.Delta : 0,
            MidLine = true,
            Shifted = true,
        };

    private readonly record struct ShiftedRegion(int Start, int End, int Delta, int IndentAt)
    {
        public static ShiftedRegion None => new(-1, -1, 0, -1);
    }
}
