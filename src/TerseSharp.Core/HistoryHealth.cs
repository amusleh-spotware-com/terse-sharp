using System.Collections.Concurrent;

namespace TerseSharp.Core;

public static class HistoryHealth
{
    private static readonly ConcurrentDictionary<string, HistoryLog> Logs = new(StringComparer.Ordinal);

    public static IReadOnlyList<string> LogArguments { get; } =
        ["--no-optional-locks", "-c", "core.quotePath=false", "log", "--max-count=200", "--no-renames", "--no-color", "--relative", "--name-only", "--format=%x00%aN"];

    public static bool Wanted(PolicyOptions options, IReadOnlyList<string> ids) =>
        options.Enforces(PolicyRule.CodeHealth) && Admits(ids);

    public static bool Holds(string root, string head) =>
        Logs.TryGetValue(root, out var cached) && cached.Head.Equals(head, StringComparison.Ordinal);

    public static void Remember(string root, string head, string log) => Logs[root] = new HistoryLog(head, log);

    public static async Task<IReadOnlyList<string>> FindingsAsync(LoadedWorkspace workspace, DiagnosticScope scope, CancellationToken cancellationToken)
    {
        var options = await PolicyCache.ForAsync(workspace.Root, cancellationToken).ConfigureAwait(false);

        return options.Enforces(PolicyRule.CodeHealth) && Logs.TryGetValue(workspace.Root, out var cached)
            ? Findings(cached.Log, workspace.Root, scope.Files ?? Documents(workspace), options.Limit(PolicyRule.CodeHealth).Action)
            : [];
    }

    public static IReadOnlyList<string> Findings(string log, string root, IEnumerable<string> files, PolicyAction action)
    {
        var tracked = HistoryScan.Tracked(root, files);

        HistoryScan.Scan(log.AsSpan(), tracked.GetAlternateLookup<ReadOnlySpan<char>>());

        return [.. tracked.Values.SelectMany(history => HistorySmells.Found(root, history, action))];
    }

    private static bool Admits(IReadOnlyList<string> ids) =>
        ids.Count is 0 || ids.Any(id => PolicyRules.Of(PolicyRule.CodeHealth).Id.AsSpan().StartsWith(DiagnosticIds.Stem(id), StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Documents(LoadedWorkspace workspace) =>
        workspace.Solution.Projects.SelectMany(project => project.Documents).Select(document => document.FilePath).OfType<string>();

    private sealed record HistoryLog(string Head, string Log);
}

internal static class HistoryScan
{
    public static Dictionary<string, FileHistory> Tracked(string root, IEnumerable<string> files)
    {
        Dictionary<string, FileHistory> tracked = [with(StringComparer.OrdinalIgnoreCase)];

        foreach (var file in files)
        {
            var key = Path.GetRelativePath(root, file).Replace('\\', '/');

            tracked.TryAdd(key, new FileHistory(file, key));
        }

        return tracked;
    }

    public static void Scan(ReadOnlySpan<char> log, Dictionary<string, FileHistory>.AlternateLookup<ReadOnlySpan<char>> tracked)
    {
        List<FileHistory> touched = [];

        foreach (var range in log.Split('\0'))
        {
            var commit = log[range];
            var header = commit.IndexOf('\n');

            if (header < 0)
                continue;

            Touched(commit[(header + 1)..], tracked, touched);
            Record(commit[..header].TrimEnd('\r'), commit[(header + 1)..], touched);
            touched.Clear();
        }
    }

    private static void Touched(ReadOnlySpan<char> paths, Dictionary<string, FileHistory>.AlternateLookup<ReadOnlySpan<char>> tracked, List<FileHistory> touched)
    {
        foreach (var range in paths.Split('\n'))
        {
            if (tracked.TryGetValue(paths[range].TrimEnd('\r'), out var history))
                touched.Add(history);
        }
    }

    private static void Record(ReadOnlySpan<char> author, ReadOnlySpan<char> paths, List<FileHistory> touched)
    {
        foreach (var history in touched)
        {
            history.Commits++;
            history.Authors.GetAlternateLookup<ReadOnlySpan<char>>().Add(author);
            Partners(paths, history);
        }
    }

    private static void Partners(ReadOnlySpan<char> paths, FileHistory history)
    {
        var tally = history.Partners.GetAlternateLookup<ReadOnlySpan<char>>();

        foreach (var range in paths.Split('\n'))
        {
            var path = paths[range].TrimEnd('\r');

            if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !path.Equals(history.Key, StringComparison.OrdinalIgnoreCase))
                tally[path] = tally.TryGetValue(path, out var seen) ? seen + 1 : 1;
        }
    }
}

internal static class HistorySmells
{
    private const int Window = 200;

    private const int CongestedAuthors = 3;

    private const int CoupledCommits = 3;

    private const int CoupledPercent = 50;

    public static IEnumerable<string> Found(string root, FileHistory history, PolicyAction action)
    {
        var path = PositionFormat.Relative(root, history.File);

        if (history.Authors.Count > CongestedAuthors)
            yield return Congestion(path, history.Authors.Count, action);

        if (Coupled(history) is { } partner)
            yield return Coupling(path, action, history.Commits, partner);
    }

    private static KeyValuePair<string, int>? Coupled(FileHistory history) =>
        history.Partners
            .Where(pair => pair.Value >= CoupledCommits && pair.Value * 100 >= history.Commits * CoupledPercent)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Cast<KeyValuePair<string, int>?>()
            .FirstOrDefault();

    private static string Congestion(string path, int authors, PolicyAction action) => new PolicyFinding(
        PolicyRule.CodeHealth,
        action,
        path,
        1,
        1,
        "<file>",
        string.Create(CultureInfo.InvariantCulture, $"developer congestion HEURISTIC: {authors} authors in the last {Window} commits"),
        CongestedAuthors.ToString(CultureInfo.InvariantCulture)).Diagnostic();

    private static string Coupling(string path, PolicyAction action, int commits, KeyValuePair<string, int> partner) => new PolicyFinding(
        PolicyRule.CodeHealth,
        action,
        path,
        1,
        1,
        "<file>",
        string.Create(CultureInfo.InvariantCulture, $"change coupling HEURISTIC: co-changed with {partner.Key.Replace('/', Path.DirectorySeparatorChar)} in {partner.Value} of its {commits} commits"),
        string.Create(CultureInfo.InvariantCulture, $"{CoupledPercent}%"),
        "reaches").Diagnostic();
}

internal sealed class FileHistory(string file, string key)
{
    public string File { get; } = file;

    public string Key { get; } = key;

    public int Commits { get; set; }

    public HashSet<string> Authors { get; } = [];

    public Dictionary<string, int> Partners { get; } = [];
}
