using System.Diagnostics;

namespace TerseSharp.Server;

public sealed class ListingMemo
{
    private const int MaxRemembered = 32;

    private readonly Lock gate = new();
    private readonly Dictionary<string, Entry> listings = new(MaxRemembered, StringComparer.Ordinal);

    public TimeSpan SlowAfter { get; init; } = Configured(Environment.GetEnvironmentVariable("TERSE_LISTING_TIMING_MS"));

    private static TimeSpan Configured(string? milliseconds) => long.TryParse(milliseconds, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed <= int.MaxValue
        ? TimeSpan.FromMilliseconds(parsed)
        : TimeSpan.FromSeconds(10);

    private readonly record struct Entry(string? Stamp, string Response, long Timestamp);

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

    public string? Remember(string key, string? stamp, string? unavailable, string response, long timestamp)
    {
        lock (gate)
        {
            var note = listings.TryGetValue(key, out var previous) ? Moved(previous.Stamp, stamp, unavailable) : null;

            if (listings.Count >= MaxRemembered && !listings.ContainsKey(key))
                listings.Clear();

            listings[key] = new Entry(stamp, response, timestamp);

            return note;
        }
    }

    private static long Seconds(long ticks) => Math.Max(0, ticks) / Stopwatch.Frequency;

    private static string? Moved(string? remembered, string? current, string? unavailable) => (remembered, current) switch
    {
        (_, null) => string.Concat("NOTE re-ran: no stamp ", unavailable),
        (null, _) => "NOTE re-ran: the previous call took no stamp",
        ({ } before, { } after) when !string.Equals(before, after, StringComparison.Ordinal) => string.Concat("NOTE re-ran: stamp moved ", StampMove.Named(before, after)),
        _ => null,
    };
}
