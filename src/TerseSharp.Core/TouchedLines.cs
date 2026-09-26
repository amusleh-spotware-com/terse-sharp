using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public sealed class TouchedLines
{
    private readonly Dictionary<string, List<DiffHunk>> hunks;

    private TouchedLines(Dictionary<string, List<DiffHunk>> hunks) => this.hunks = hunks;

    public static TouchedLines From(string unifiedDiff, IReadOnlyList<string> untracked, string root)
    {
        var grouped = new Dictionary<string, List<DiffHunk>>(StringComparer.FromComparison(PathBoundary.Comparison));

        foreach (var hunk in DiffParser.Hunks(unifiedDiff))
        {
            Bucket(grouped, Keyed(hunk.Path)).Add(hunk);
            Bucket(grouped, Keyed(Path.GetFullPath(Path.Combine(root, hunk.Path)))).Add(hunk);
        }

        var touched = new TouchedLines(grouped);

        foreach (var path in untracked)
        {
            if (path.Length > 0)
                touched.Add(root, path);
        }

        return touched;
    }

    private static List<DiffHunk> Bucket(Dictionary<string, List<DiffHunk>> grouped, string key)
    {
        if (grouped.TryGetValue(key, out var bucket))
            return bucket;

        bucket = [];
        grouped[key] = bucket;

        return bucket;
    }

    public bool Covers(string path, int line)
    {
        if (Known(path, line))
            return true;

        var full = Path.IsPathRooted(path) ? Path.GetFullPath(path) : path;

        return !string.Equals(full, path, StringComparison.Ordinal) && Known(full, line);
    }

    public bool Covers(Diagnostic diagnostic)
    {
        if (diagnostic.Location.SourceTree is not { FilePath.Length: > 0 } tree)
            return true;

        return Covers(Path.GetFullPath(tree.FilePath), diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1);
    }

    public bool CoversRecord(string record)
    {
        if (PolicyPosition.Locate(record) is not { } position)
            return true;

        return Covers(position.Path, position.Line);
    }

    private readonly HashSet<string> whole = new(StringComparer.FromComparison(PathBoundary.Comparison));

    private void Add(string root, string path)
    {
        whole.Add(Keyed(path));
        whole.Add(Keyed(Path.GetFullPath(Path.Combine(root, path))));
    }

    private bool Known(string path, int line)
    {
        var keyed = Keyed(path);

        if (whole.Contains(keyed))
            return true;

        if (!hunks.TryGetValue(keyed, out var bucket))
            return false;

        foreach (var hunk in bucket)
        {
            if (line >= hunk.Start && line <= hunk.End)
                return true;
        }

        return false;
    }

    private static string Keyed(string path) => path.Replace('\\', '/');

    public bool Touches(string? path) =>
        path is { Length: > 0 } && Holds(Keyed(Path.GetFullPath(path)));

    private bool Holds(string keyed) => whole.Contains(keyed) || hunks.ContainsKey(keyed);
}

public readonly record struct RecordPosition(string Path, int Line);

public static class PolicyPosition
{
    public static RecordPosition? Locate(string record)
    {
        var message = record.IndexOf(": ", StringComparison.Ordinal);

        if (message < 0)
            return null;

        var head = record.AsSpan(0, message);
        var space = head.LastIndexOf(' ');

        if (space < 0)
            return null;

        var located = head[(space + 1)..];
        var column = located.LastIndexOf(':');

        if (column <= 0)
            return null;

        var line = located[..column].LastIndexOf(':');

        return line > 0 && int.TryParse(located[(line + 1)..column], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? new RecordPosition(new string(located[..line]), number)
            : null;
    }
}
