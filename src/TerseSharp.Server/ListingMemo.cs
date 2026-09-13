using System.Diagnostics;

namespace TerseSharp.Server;

public sealed class ListingMemo
{
    private const int MaxRemembered = 32;

    private readonly Lock gate = new();
    private readonly Dictionary<string, Entry> listings = new(MaxRemembered, StringComparer.Ordinal);

    private readonly record struct Entry(string Stamp, string Response, long Timestamp);

    public string? Replay(string key, string stamp, long timestamp)
    {
        lock (gate)
        {
            return listings.TryGetValue(key, out var entry) && string.Equals(entry.Stamp, stamp, StringComparison.Ordinal)
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{entry.Response}\nUNCHANGED - no watcher event and no git state change since this exact call {Seconds(timestamp - entry.Timestamp)}s ago")
                : null;
        }
    }

    public void Remember(string key, string stamp, string response, long timestamp)
    {
        lock (gate)
        {
            if (listings.Count >= MaxRemembered && !listings.ContainsKey(key))
                listings.Clear();

            listings[key] = new Entry(stamp, response, timestamp);
        }
    }

    private static long Seconds(long ticks) => Math.Max(0, ticks) / Stopwatch.Frequency;
}
