namespace TerseSharp.Core;

public readonly record struct SnippetMatch(int Start, int Length, int Occurrences, bool Normalized)
{
    public bool IsUnique => Occurrences is 1;

    public string? Indent { get; init; }
}

public static class SnippetSearch
{
    private const int NearMissWidth = 100;

    private const int MinSharedPrefix = 6;

    public static SnippetMatch Find(string haystack, string needle, int occurrence)
    {
        if (needle.Length is 0)
            return default;

        var exact = Locate(haystack, needle, occurrence);

        if (exact.Occurrences > 0)
            return exact;

        var relaxed = Relaxed(haystack, needle, occurrence);

        if (relaxed.Occurrences > 0)
            return relaxed;

        var reindented = Reindented(haystack, needle, occurrence);

        return reindented.Occurrences > 0 ? reindented : relaxed;
    }

    public static int Count(ReadOnlySpan<char> text, ReadOnlySpan<char> value) => Locate(text, value, 1).Occurrences;

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

    private static SnippetMatch Relaxed(string haystack, string needle, int occurrence)
    {
        var text = LineEndings.Normalize(haystack);
        var value = LineEndings.Normalize(needle);

        if (ReferenceEquals(text, haystack) && ReferenceEquals(value, needle))
            return new SnippetMatch(-1, needle.Length, 0, false);

        var found = Locate(text, value, occurrence);

        return found.Start >= 0 ? Mapped(haystack, found) : found with { Normalized = true };
    }

    private static SnippetMatch Mapped(string haystack, SnippetMatch found)
    {
        var start = LineEndings.OriginalOffset(haystack, found.Start);
        var end = LineEndings.OriginalOffset(haystack, found.Start + found.Length);

        return new SnippetMatch(start, end - start, found.Occurrences, true);
    }

    private static SnippetMatch Locate(ReadOnlySpan<char> text, ReadOnlySpan<char> value, int occurrence)
    {
        var occurrences = 0;
        var chosen = -1;
        var start = 0;

        while (start <= text.Length && text[start..].IndexOf(value, StringComparison.Ordinal) is var offset and >= 0)
        {
            occurrences++;

            if (occurrences == occurrence)
                chosen = start + offset;

            start += offset + value.Length;
        }

        return new SnippetMatch(chosen, value.Length, occurrences, false);
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

    public static IReadOnlyList<string> Sites(string haystack, string needle, int maxResults)
    {
        var text = haystack.AsSpan();
        var value = needle.AsSpan();
        var sites = new List<string>(maxResults);
        var start = 0;
        var index = 0;

        while (value.Length > 0 && start <= text.Length && text[start..].IndexOf(value, StringComparison.Ordinal) is var offset and >= 0)
        {
            var at = start + offset;

            index++;

            if (sites.Count < maxResults)
                sites.Add(Site(text, at, index));

            start = at + value.Length;
        }

        return sites;
    }

    private static string Site(ReadOnlySpan<char> text, int at, int index) => string.Create(
        CultureInfo.InvariantCulture,
        $"  occurrence={index}  line {LineNumber(text, at)}: {Clip(LineAround(text, at))}");

    private static int LineNumber(ReadOnlySpan<char> text, int at)
    {
        var lines = 1;

        foreach (var character in text[..at])
        {
            if (character is '\n')
                lines++;
        }

        return lines;
    }

    private static ReadOnlySpan<char> LineAround(ReadOnlySpan<char> text, int at)
    {
        var start = text[..at].LastIndexOf('\n') + 1;
        var end = text[at..].IndexOf('\n');

        return text[start..(end < 0 ? text.Length : at + end)].TrimEnd('\r');
    }

    private readonly record struct IndentedRegion(int End, string? Indent);

    private static int LineEnd(ReadOnlySpan<char> text, int at) =>
        text[at..].IndexOf('\n') is var offset and >= 0 ? at + offset : text.Length;

    private static bool Adopted(ReadOnlySpan<char> line, ReadOnlySpan<char> text, ref string? indent)
    {
        var pad = line.Length - text.Length;

        if (pad <= 0 || !line[..pad].IsWhiteSpace())
            return false;

        indent = new string(line[..pad]);

        return true;
    }

    private static bool SameLine(ReadOnlySpan<char> candidate, ReadOnlySpan<char> needle, ref string? indent)
    {
        var line = candidate.TrimEnd();
        var text = needle.TrimEnd();

        if (text.IsEmpty)
            return line.IsEmpty;

        if (indent is null && !Adopted(line, text, ref indent))
            return false;

        return line.Length == indent!.Length + text.Length
            && line.StartsWith(indent, StringComparison.Ordinal)
            && line.EndsWith(text, StringComparison.Ordinal);
    }

    private static int Closed(ReadOnlySpan<char> text, int end, bool trailing) =>
        trailing && end < text.Length ? end + 1 : end;

    private static IndentedRegion MatchedRegion(ReadOnlySpan<char> text, int start, ReadOnlySpan<char> value)
    {
        var trailing = value.EndsWith("\n", StringComparison.Ordinal);
        var body = trailing ? value[..^1] : value;
        var at = start;
        var end = -1;
        string? indent = null;

        foreach (var needle in body.EnumerateLines())
        {
            end = LineEnd(text, at);

            if (!SameLine(text[at..end], needle, ref indent))
                return new IndentedRegion(-1, null);

            at = end < text.Length ? end + 1 : end;
        }

        return indent is null ? new IndentedRegion(-1, null) : new IndentedRegion(Closed(text, end, trailing), indent);
    }

    private static SnippetMatch LocateReindented(ReadOnlySpan<char> text, ReadOnlySpan<char> value, int occurrence)
    {
        var found = new IndentedRegion(-1, null);
        var chosen = -1;
        var occurrences = 0;
        var start = 0;

        while (start <= text.Length)
        {
            var region = MatchedRegion(text, start, value);

            if (region.End >= 0 && ++occurrences == occurrence)
                (chosen, found) = (start, region);

            if (text[start..].IndexOf('\n') is var offset and >= 0)
                start += offset + 1;
            else
                break;
        }

        return new SnippetMatch(chosen, chosen >= 0 ? found.End - chosen : value.Length, occurrences, false) { Indent = found.Indent };
    }

    private static SnippetMatch Reindented(string haystack, string needle, int occurrence)
    {
        var text = LineEndings.Normalize(haystack);
        var value = LineEndings.Normalize(needle);
        var found = LocateReindented(text, value, occurrence);

        return found.Start < 0 || ReferenceEquals(text, haystack)
            ? found
            : Mapped(haystack, found) with { Indent = found.Indent };
    }

    private const int MaxRegionLines = 40;
    private const int MaxScannedLines = 5000;

    private readonly record struct RegionScore(int Start, int Matched);

    private static List<string> Bare(string text, int max)
    {
        var lines = new List<string>(64);

        foreach (var line in text.AsSpan().EnumerateLines())
        {
            lines.Add(new string(line.Trim()));

            if (lines.Count == max)
                break;
        }

        if (text.EndsWith('\n') && lines.Count > 0 && lines[^1].Length is 0)
            lines.RemoveAt(lines.Count - 1);

        return lines;
    }

    private static int Overlapping(List<string> have, List<string> wanted, int start)
    {
        var matched = 0;

        for (var index = 0; index < wanted.Count; index++)
        {
            if (wanted[index].Length > 0 && string.Equals(have[start + index], wanted[index], StringComparison.Ordinal))
                matched++;
        }

        return matched;
    }

    private static RegionScore Best(List<string> have, List<string> wanted)
    {
        var best = new RegionScore(0, 0);

        for (var start = 0; start + wanted.Count <= have.Count; start++)
        {
            if (Overlapping(have, wanted, start) is var matched && matched > best.Matched)
                best = new RegionScore(start, matched);
        }

        return best;
    }

    public static string NearestRegion(string haystack, string needle)
    {
        var wanted = Bare(needle, MaxRegionLines);

        if (wanted.Count < 2)
            return string.Empty;

        var best = Best(Bare(haystack, MaxScannedLines), wanted);

        return best.Matched >= 2 && best.Matched * 2 >= wanted.Count
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"the file's closest region is lines {best.Start + 1}-{best.Start + wanted.Count}, where {best.Matched} of the anchor's {wanted.Count} lines match - re-read exactly that with read_text startLine={best.Start + 1} endLine={best.Start + wanted.Count} verbose=true and copy the anchor from it")
            : string.Empty;
    }
}
