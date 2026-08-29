using System.Diagnostics;

namespace TerseSharp.Server;

public sealed class UnchangedRun
{
    private const int MaxRemembered = 64;

    private readonly Lock gate = new();
    private readonly Dictionary<string, Seen> runs = new(MaxRemembered, StringComparer.Ordinal);

    public string? Replay(string key, string stamp, long timestamp)
    {
        lock (gate)
        {
            return runs.TryGetValue(key, out var seen) && string.Equals(seen.Stamp, stamp, StringComparison.Ordinal)
                ? Rendered(seen, timestamp)
                : null;
        }
    }

    public void Remember(string key, string stamp, string verdict, long timestamp)
    {
        lock (gate)
        {
            if (runs.Count >= MaxRemembered && !runs.ContainsKey(key))
                runs.Clear();

            runs[key] = new Seen(stamp, verdict, timestamp);
        }
    }

    public void Forget()
    {
        lock (gate)
            runs.Clear();
    }

    private static string Rendered(Seen seen, long timestamp) => string.Create(
        CultureInfo.InvariantCulture,
        $"run_tests UNCHANGED  nothing was written since this exact call {Seconds(timestamp - seen.Timestamp)}s ago - previous: {seen.Verdict} - force=true re-runs it");

    private static long Seconds(long ticks) => Math.Max(0, ticks) / Stopwatch.Frequency;

    private readonly record struct Seen(string Stamp, string Verdict, long Timestamp);
}
