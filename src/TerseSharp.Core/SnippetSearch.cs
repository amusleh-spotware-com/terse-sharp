namespace TerseSharp.Core;

public readonly record struct SnippetMatch(int Start, int Length, int Occurrences, bool Normalized)
{
    public bool IsUnique => Occurrences is 1;
}

public static class SnippetSearch
{
    private const int NearMissWidth = 100;

    private const int MinSharedPrefix = 6;

    public static SnippetMatch Find(string haystack, string needle)
    {
        if (needle.Length is 0)
            return default;

        var exact = Locate(haystack, needle);

        return exact.Occurrences is 0 ? Relaxed(haystack, needle) : exact;
    }

    public static int Count(ReadOnlySpan<char> text, ReadOnlySpan<char> value) => Locate(text, value).Occurrences;

    public static IReadOnlyList<string> NearMisses(string haystack, string needle, int maxResults)
    {
        var anchor = Anchor(needle);

        if (anchor.Length is 0)
            return [];

        var hits = new List<string>(maxResults);
        var number = 0;

        foreach (var line in haystack.AsSpan().EnumerateLines())
        {
            number++;

            if (hits.Count < maxResults && Resembles(line.Trim(), anchor))
                hits.Add(string.Create(CultureInfo.InvariantCulture, $"L{number}: {Clip(line.Trim())}"));
        }

        return hits;
    }

    private static SnippetMatch Relaxed(string haystack, string needle)
    {
        var text = LineEndings.Normalize(haystack);
        var value = LineEndings.Normalize(needle);

        if (ReferenceEquals(text, haystack) && ReferenceEquals(value, needle))
            return new SnippetMatch(-1, needle.Length, 0, false);

        var found = Locate(text, value);

        return found.IsUnique ? Mapped(haystack, found) : found with { Normalized = true };
    }

    private static SnippetMatch Mapped(string haystack, SnippetMatch found)
    {
        var start = LineEndings.OriginalOffset(haystack, found.Start);
        var end = LineEndings.OriginalOffset(haystack, found.Start + found.Length);

        return new SnippetMatch(start, end - start, 1, true);
    }

    private static SnippetMatch Locate(ReadOnlySpan<char> text, ReadOnlySpan<char> value)
    {
        var occurrences = 0;
        var first = -1;
        var start = 0;

        while (start <= text.Length && text[start..].IndexOf(value, StringComparison.Ordinal) is var offset and >= 0)
        {
            if (occurrences is 0)
                first = start + offset;

            occurrences++;
            start += offset + value.Length;
        }

        return new SnippetMatch(first, value.Length, occurrences, false);
    }

    private static string Anchor(string needle)
    {
        foreach (var line in needle.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();

            if (trimmed.Length >= 4)
                return new string(trimmed);
        }

        return string.Empty;
    }

    private static string Clip(ReadOnlySpan<char> line) => line.Length <= NearMissWidth
        ? new string(line)
        : string.Create(CultureInfo.InvariantCulture, $"{line[..NearMissWidth]}... (+{line.Length - NearMissWidth} chars)");
    private static bool Resembles(ReadOnlySpan<char> line, string anchor)
    {
        if (line.Length is 0)
            return false;

        var shared = 0;

        while (shared < line.Length && shared < anchor.Length && line[shared] == anchor[shared])
            shared++;

        return shared >= MinSharedPrefix || line.Contains(anchor, StringComparison.Ordinal);
    }
}
