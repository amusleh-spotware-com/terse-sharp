using System.Diagnostics;

namespace TerseSharp.Server;

public sealed class UnchangedRun
{
    private const int MaxRemembered = 64;

    private readonly Lock gate = new();
    private readonly Dictionary<string, Seen> runs = new(MaxRemembered, StringComparer.Ordinal);

    public string? Replay(string tool, string key, string stamp, long timestamp)
    {
        lock (gate)
        {
            return runs.TryGetValue(key, out var seen) && string.Equals(seen.Stamp, stamp, StringComparison.Ordinal)
                ? Rendered(tool, seen, timestamp)
                : null;
        }
    }

    public string? Verbatim(string tool, string key, string stamp, long timestamp)
    {
        lock (gate)
        {
            return runs.TryGetValue(key, out var seen) && string.Equals(seen.Stamp, stamp, StringComparison.Ordinal)
                ? Replayed(tool, seen, timestamp)
                : null;
        }
    }

    private static string Replayed(string tool, Seen seen, long timestamp) => string.Create(
        CultureInfo.InvariantCulture,
        $"{seen.Verdict}\nNOTE {tool} UNCHANGED - nothing was written since this exact call {Seconds(timestamp - seen.Timestamp)}s ago, so this is that answer replayed; force=true re-runs it");

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

    private static string Rendered(string tool, Seen seen, long timestamp) => string.Create(
        CultureInfo.InvariantCulture,
        $"{tool} UNCHANGED  nothing was written since this exact call {Seconds(timestamp - seen.Timestamp)}s ago - previous: {seen.Verdict} - force=true re-runs it");

    private static long Seconds(long ticks) => Math.Max(0, ticks) / Stopwatch.Frequency;

    private readonly record struct Seen(string Stamp, string Verdict, long Timestamp);

    public string MissReason(string key, string stamp)
    {
        lock (gate)
        {
            return runs.TryGetValue(key, out var seen)
                ? "stamp moved: " + FirstDifference(seen.Stamp, stamp)
                : "first run of this key";
        }
    }

    public string? RerunNote(string key, string stamp)
    {
        lock (gate)
        {
            return runs.TryGetValue(key, out var seen) && !string.Equals(seen.Stamp, stamp, StringComparison.Ordinal)
                ? string.Create(CultureInfo.InvariantCulture, $"NOTE re-ran: stamp moved {StampMove.Named(seen.Stamp, stamp)}")
                : null;
        }
    }

    public static string? MemoRefusal(string green, string verdict) =>
        !verdict.StartsWith(green, StringComparison.Ordinal)
            ? string.Create(CultureInfo.InvariantCulture, $"the verdict does not open with '{green}'")
            : verdict.Contains('\n', StringComparison.Ordinal)
                ? "the verdict is not a single line"
                : null;

    private static string FirstDifference(string remembered, string current)
    {
        var before = remembered.Split(' ');
        var after = current.Split(' ');

        for (var index = 0; index < Math.Min(before.Length, after.Length); index++)
        {
            if (!string.Equals(before[index], after[index], StringComparison.Ordinal))
                return string.Create(CultureInfo.InvariantCulture, $"remembered '{before[index]}' current '{after[index]}'");
        }

        return string.Create(CultureInfo.InvariantCulture, $"remembered '{remembered}' current '{current}'");
    }
}
