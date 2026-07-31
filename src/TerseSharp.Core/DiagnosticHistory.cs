using System.Collections.Concurrent;

namespace TerseSharp.Core;

public sealed record DiagnosticDelta(IReadOnlyList<string> Appeared, IReadOnlyList<string> Fixed, int Unchanged, bool Baseline);

public static class DiagnosticHistory
{
    private const int MaxScopes = 64;

    private static readonly ConcurrentDictionary<string, HashSet<string>> Previous = new(StringComparer.Ordinal);
    private static readonly Lock Gate = new();

    public static DiagnosticDelta Record(string scope, IReadOnlyList<string> current)
    {
        lock (Gate)
        {
            var now = new HashSet<string>(current, StringComparer.Ordinal);
            var had = Previous.TryGetValue(scope, out var stored) ? stored : null;

            Evict();
            Previous[scope] = now;

            return had is null
                ? new DiagnosticDelta(current, [], 0, Baseline: true)
                : new DiagnosticDelta(
                    [.. current.Where(entry => !had.Contains(entry))],
                    [.. had.Where(entry => !now.Contains(entry)).Order(StringComparer.Ordinal)],
                    current.Count(had.Contains),
                    Baseline: false);
        }
    }

    private static void Evict()
    {
        if (Previous.Count < MaxScopes)
            return;

        foreach (var key in Previous.Keys.Take(Previous.Count - MaxScopes + 1))
            Previous.TryRemove(key, out _);
    }

    public static bool Knows(string scope) => Previous.ContainsKey(scope);

    public static void Forget() => Previous.Clear();
}
