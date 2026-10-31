namespace TerseSharp.Core;

public static class CoChange
{
    private const int Shown = 5;

    private const int MinimumCommits = 2;

    public static IReadOnlyList<string> LogArguments(IReadOnlyList<string> declaring) =>
        ["--no-optional-locks", "-c", "core.quotePath=false", "log", "--max-count=200", "--no-renames", "--no-color", "--full-diff", "--relative", "--name-only", "--format=%x00", "--", .. declaring];

    public static IReadOnlyList<string> Lines(string log, IEnumerable<string> declaring, IEnumerable<string> listed)
    {
        var owned = Slashed(declaring);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tally = counts.GetAlternateLookup<ReadOnlySpan<char>>();
        var own = owned.GetAlternateLookup<ReadOnlySpan<char>>();
        var text = log.AsSpan();

        foreach (var commit in text.Split('\0'))
            Tally(text[commit], own, tally);

        return Ranked(counts, Slashed(listed));
    }

    private static HashSet<string> Slashed(IEnumerable<string> paths) =>
        new(paths.Select(path => path.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);

    private static void Tally(
        ReadOnlySpan<char> commit,
        HashSet<string>.AlternateLookup<ReadOnlySpan<char>> own,
        Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> tally)
    {
        if (!Touches(commit, own))
            return;

        foreach (var line in commit.Split('\n'))
        {
            var path = commit[line].TrimEnd('\r');

            if (path.Length > 0 && !own.Contains(path))
                tally[path] = tally.TryGetValue(path, out var seen) ? seen + 1 : 1;
        }
    }

    private static bool Touches(ReadOnlySpan<char> commit, HashSet<string>.AlternateLookup<ReadOnlySpan<char>> own)
    {
        foreach (var line in commit.Split('\n'))
        {
            if (own.Contains(commit[line].TrimEnd('\r')))
                return true;
        }

        return false;
    }

    private static IReadOnlyList<string> Ranked(Dictionary<string, int> counts, HashSet<string> listed) =>
    [
        .. counts
            .Where(pair => pair.Value >= MinimumCommits && !listed.Contains(pair.Key))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(Shown)
            .Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key.Replace('/', Path.DirectorySeparatorChar)}  HEURISTIC  co-changed  commits={pair.Value}")),
    ];
}
