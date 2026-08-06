namespace TerseSharp.Core;

public readonly record struct DiffHunk(string Path, int Start, int Count)
{
    public int End => Count is 0 ? Start : Start + Count - 1;
}

public readonly record struct ChangedFile(string Path, int Added, int Deleted, string Status);

public static class DiffParser
{
    public static IReadOnlyList<DiffHunk> Hunks(string unifiedDiff)
    {
        var hunks = new List<DiffHunk>(32);
        var path = string.Empty;

        foreach (var line in unifiedDiff.AsSpan().EnumerateLines())
        {
            if (line.StartsWith("+++ ", StringComparison.Ordinal))
                path = Target(line[4..]);
            else if (path.Length > 0 && line.StartsWith("@@ ", StringComparison.Ordinal) && Added(line, path) is { } hunk)
                hunks.Add(hunk);
        }

        return hunks;
    }

    public static IReadOnlyList<ChangedFile> NumStat(string numstat)
    {
        var files = new List<ChangedFile>(32);

        foreach (var line in numstat.AsSpan().EnumerateLines())
        {
            if (Counted(line) is { } file)
                files.Add(file);
        }

        return files;
    }

    public static IReadOnlyDictionary<string, string> NameStatus(string nameStatus)
    {
        var statuses = new Dictionary<string, string>(32, StringComparer.Ordinal);

        foreach (var line in nameStatus.AsSpan().EnumerateLines())
        {
            var tab = line.IndexOf('\t');

            if (tab > 0)
                statuses[new string(line[(tab + 1)..].Trim())] = new string(line[..tab].Trim());
        }

        return statuses;
    }

    private static ChangedFile? Counted(ReadOnlySpan<char> line)
    {
        var firstTab = line.IndexOf('\t');

        if (firstTab <= 0)
            return null;

        var rest = line[(firstTab + 1)..];
        var secondTab = rest.IndexOf('\t');

        if (secondTab < 0)
            return null;

        return new ChangedFile(
            new string(rest[(secondTab + 1)..].Trim()),
            Number(line[..firstTab]),
            Number(rest[..secondTab]),
            "M");
    }

    private static int Number(ReadOnlySpan<char> text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1;

    private static string Target(ReadOnlySpan<char> text)
    {
        var trimmed = text.Trim();

        if (trimmed.SequenceEqual("/dev/null"))
            return string.Empty;

        return new string(trimmed.StartsWith("b/", StringComparison.Ordinal) ? trimmed[2..] : trimmed);
    }

    private static DiffHunk? Added(ReadOnlySpan<char> line, string path)
    {
        var plus = line.IndexOf('+');

        if (plus < 0)
            return null;

        var rest = line[(plus + 1)..];
        var end = rest.IndexOfAny(" ,".AsSpan());

        if (end < 0)
            return null;

        var start = Number(rest[..end]);

        if (start < 0)
            return null;

        return new DiffHunk(path, start, rest[end] is ',' ? Length(rest[(end + 1)..]) : 1);
    }

    private static int Length(ReadOnlySpan<char> text)
    {
        var end = text.IndexOf(' ');

        return Math.Max(0, Number(end < 0 ? text : text[..end]));
    }
}
