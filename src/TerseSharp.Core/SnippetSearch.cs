namespace TerseSharp.Core;

public readonly record struct SnippetMatch(int Start, int Length, int Occurrences, bool Normalized)
{
    public bool IsUnique => Occurrences is 1;

    public string? Indent { get; init; }

    public bool Loose { get; init; }

    public bool MidLine { get; init; }
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

        return reindented.Occurrences > 0 ? reindented
            : Loose(haystack, needle, occurrence) is { Occurrences: > 0 } loose ? loose
            : relaxed;
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

    private static int LineEnd(ReadOnlySpan<char> text, int at) =>
        text[at..].IndexOf('\n') is var offset and >= 0 ? at + offset : text.Length;

    private static bool Adopted(ReadOnlySpan<char> line, ReadOnlySpan<char> text, ref int pad)
    {
        var width = line.Length - text.Length;

        if (width <= 0 || !line[..width].IsWhiteSpace())
            return false;

        pad = width;

        return true;
    }

    private static bool SameLine(ReadOnlySpan<char> candidate, ReadOnlySpan<char> needle, ReadOnlySpan<char> establishedIndent, ref int pad)
    {
        var line = candidate.TrimEnd();
        var text = needle.TrimEnd();

        if (text.IsEmpty)
            return line.IsEmpty;

        if (pad < 0 && !Adopted(line, text, ref pad))
            return false;

        var indent = establishedIndent.IsEmpty ? line[..pad] : establishedIndent;

        return line.Length == pad + text.Length
            && line.StartsWith(indent, StringComparison.Ordinal)
            && line.EndsWith(text, StringComparison.Ordinal);
    }

    private static int Closed(ReadOnlySpan<char> text, int end, bool trailing) =>
        trailing && end < text.Length ? end + 1 : end;

    private static SnippetMatch LocateReindented(ReadOnlySpan<char> text, ReadOnlySpan<char> value, int occurrence)
    {
        var found = AnchorRegion.None;
        var occurrences = 0;
        var start = 0;

        while (start <= text.Length)
        {
            if (AnchorAt(text, start, value) is { End: >= 0 } region && ++occurrences == occurrence)
                found = region;

            if (text[start..].IndexOf('\n') is var offset and >= 0)
                start += offset + 1;
            else
                break;
        }

        var indent = found.IndentStart >= 0 ? text.Slice(found.IndentStart, found.IndentLength).ToString() : null;

        return new SnippetMatch(found.Start, found.End >= 0 ? found.End - found.Start : value.Length, occurrences, false) { Indent = indent, MidLine = found.MidLine };
    }

    private static SnippetMatch Reindented(string haystack, string needle, int occurrence)
    {
        var text = LineEndings.Normalize(haystack);
        var value = LineEndings.Normalize(needle);
        var found = LocateReindented(text, value, occurrence);

        return found.Start < 0 || ReferenceEquals(text, haystack)
            ? found
            : Mapped(haystack, found) with { Indent = found.Indent, MidLine = found.MidLine };
    }

    private enum AnchorLine
    {
        Body,
        MidLineHead,
        Tail,
    }

    private readonly record struct AnchorRegion(int Start, int End, int IndentStart, int IndentLength, bool MidLine)
    {
        public static AnchorRegion None => new(-1, -1, -1, -1, false);
    }

    private static int Depth(ReadOnlySpan<char> line) => line.Length - line.TrimStart().Length;

    private static int LastLine(ReadOnlySpan<char> body)
    {
        var last = -1;

        foreach (var _ in body.EnumerateLines())
            last++;

        return last;
    }

    private static bool SameIndent(ReadOnlySpan<char> text, ReadOnlySpan<char> lead, AnchorRegion region) =>
        region.IndentStart >= 0 ? lead.SequenceEqual(text.Slice(region.IndentStart, region.IndentLength)) : lead.IsWhiteSpace();


    private static bool Reaches(ReadOnlySpan<char> line, ReadOnlySpan<char> wanted, int pad) =>
        pad >= 0 && line.Length >= pad + wanted.Length;


    private static bool Fits(ReadOnlySpan<char> text, ReadOnlySpan<char> line, ReadOnlySpan<char> wanted, int pad, AnchorRegion region) =>
        Reaches(line, wanted, pad) && SameIndent(text, line[..pad], region) && line[pad..].StartsWith(wanted, StringComparison.Ordinal);

    private static AnchorRegion Headed(ReadOnlySpan<char> line, ReadOnlySpan<char> needle, int at, AnchorRegion region)
    {
        var wanted = needle.TrimEnd();
        var trimmed = line.TrimEnd();

        return wanted.IsEmpty || !trimmed.EndsWith(wanted, StringComparison.Ordinal)
            ? AnchorRegion.None
            : region with { Start = at + trimmed.Length - wanted.Length, End = at + line.Length, MidLine = true };
    }

    private static AnchorRegion Tailed(ReadOnlySpan<char> text, int at, int end, ReadOnlySpan<char> needle, AnchorRegion region)
    {
        var line = text[at..end];
        var wanted = needle.TrimEnd();
        var pad = region.IndentLength >= 0 ? region.IndentLength : Depth(line) - Depth(wanted);

        return wanted.IsEmpty || !Fits(text, line, wanted, pad, region)
            ? AnchorRegion.None
            : region with { End = at + pad + wanted.Length, IndentStart = region.IndentStart >= 0 ? region.IndentStart : at, IndentLength = pad };
    }

    private static AnchorRegion NextLine(ReadOnlySpan<char> text, int at, ReadOnlySpan<char> needle, AnchorRegion region, AnchorLine kind)
    {
        var end = LineEnd(text, at);

        if (kind is AnchorLine.MidLineHead)
            return Headed(text[at..end], needle, at, region);

        var pad = region.IndentLength;
        var established = region.IndentStart < 0 ? default : text.Slice(region.IndentStart, pad);

        return SameLine(text[at..end], needle, established, ref pad)
            ? region with { End = end, IndentStart = region.IndentStart < 0 && pad >= 0 ? at : region.IndentStart, IndentLength = pad }
            : kind is AnchorLine.Tail ? Tailed(text, at, end, needle, region)
            : AnchorRegion.None;
    }

    private static AnchorLine LineKind(int index, int tail, bool midLineHead) => index switch
    {
        0 when midLineHead => AnchorLine.MidLineHead,
        _ when index == tail => AnchorLine.Tail,
        _ => AnchorLine.Body,
    };

    private static AnchorRegion Walked(ReadOnlySpan<char> text, int start, ReadOnlySpan<char> body, int tail, bool midLineHead)
    {
        var region = new AnchorRegion(start, start, -1, -1, false);
        var at = start;
        var index = 0;

        foreach (var needle in body.EnumerateLines())
        {
            region = NextLine(text, at, needle, region, LineKind(index++, tail, midLineHead));

            if (region.End < 0)
                return AnchorRegion.None;

            at = region.End < text.Length ? region.End + 1 : region.End;
        }

        return region;
    }

    private static AnchorRegion AnchorAt(ReadOnlySpan<char> text, int start, ReadOnlySpan<char> value)
    {
        var trailing = value.EndsWith("\n", StringComparison.Ordinal);
        var body = trailing ? value[..^1] : value;
        var last = LastLine(body);
        var tail = trailing || last is 0 ? -1 : last;
        var region = Walked(text, start, body, tail, midLineHead: false);

        if (region.End < 0 && last > 0)
            region = Walked(text, start, body, tail, midLineHead: true);

        return region.End < 0 || region.IndentLength < 0 ? AnchorRegion.None : region with { End = Closed(text, region.End, trailing) };
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

    internal static ReadOnlySpan<char> Lead(ReadOnlySpan<char> text)
    {
        var line = text[Filled(text, 0)..];

        return line[..(line.Length - line.TrimStart().Length)];
    }

    private static SnippetMatch Loose(string haystack, string needle, int occurrence)
    {
        var text = LineEndings.Normalize(haystack);
        var found = LocateLoose(text, LineEndings.Normalize(needle), occurrence);

        return found.Start < 0 || ReferenceEquals(text, haystack)
            ? found
            : Mapped(haystack, found) with { Indent = found.Indent, Loose = true };
    }

    private static SnippetMatch LocateLoose(ReadOnlySpan<char> text, ReadOnlySpan<char> value, int occurrence)
    {
        var lead = Lead(value);
        var (chosen, stop, occurrences, start) = (-1, -1, 0, 0);

        while (start < text.Length)
        {
            if (LooseEnd(text, start, value) is var end and >= 0 && Lead(text[start..]).EndsWith(lead, StringComparison.Ordinal) && ++occurrences == occurrence)
                (chosen, stop) = (start, end);

            if (text[start..].IndexOf('\n') is var offset and >= 0)
                start += offset + 1;
            else
                break;
        }

        return chosen < 0
            ? new SnippetMatch(-1, value.Length, occurrences, false) { Loose = true }
            : Loosened(text, chosen, stop, value, lead.Length) with { Occurrences = occurrences };
    }

    private static SnippetMatch Loosened(ReadOnlySpan<char> text, int chosen, int stop, ReadOnlySpan<char> value, int lead)
    {
        var depth = Lead(text[chosen..]);
        var end = Closed(text, stop, value.EndsWith("\n", StringComparison.Ordinal));

        return new SnippetMatch(chosen, end - chosen, 0, false) { Indent = depth[..(depth.Length - lead)].ToString(), Loose = true };
    }

    private static int LooseEnd(ReadOnlySpan<char> text, int start, ReadOnlySpan<char> value)
    {
        var end = -1;

        foreach (var needle in value.EnumerateLines())
        {
            if (needle.IsWhiteSpace())
                continue;

            end = LooseLine(text, end < 0 ? start : Filled(text, end + 1), needle.Trim());

            if (end < 0)
                return -1;
        }

        return end;
    }

    private static int LooseLine(ReadOnlySpan<char> text, int at, ReadOnlySpan<char> wanted)
    {
        if (at >= text.Length)
            return -1;

        var end = LineEnd(text, at);

        return text[at..end].Trim().Equals(wanted, StringComparison.Ordinal) ? end : -1;
    }

    private static int Filled(ReadOnlySpan<char> text, int at)
    {
        while (at < text.Length)
        {
            var end = LineEnd(text, at);

            if (!text[at..end].IsWhiteSpace())
                return at;

            at = end + 1;
        }

        return text.Length;
    }
}
