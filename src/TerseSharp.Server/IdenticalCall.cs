using System.Collections.Frozen;
using System.Diagnostics;
using System.Text;
using ModelContextProtocol.Protocol;

namespace TerseSharp.Server;

public static class IdenticalCall
{
    public static readonly FrozenSet<string> Watched = new[] { "build", "run_tests", "rerun_failed", "list_tests", "clean" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, Seen> Calls = new(StringComparer.Ordinal);

    public static string? Note(string tool, CallToolRequestParams parameters, CallToolResult result) =>
        Watched.Contains(tool)
            ? Record(tool, Key(tool, parameters), Verdict(result), Stopwatch.GetTimestamp(), EditPulse.Changed)
            : null;

    internal static string? Record(string tool, string key, string verdict, long timestamp, int pulse)
    {
        lock (Gate)
        {
            var seen = Calls.TryGetValue(key, out var previous) ? previous : default;

            Calls[key] = new Seen(seen.Count + 1, timestamp, pulse, verdict);

            return seen.Count is 0 ? null : Rendered(tool, seen, timestamp, pulse);
        }
    }

    public static void Forget()
    {
        lock (Gate)
            Calls.Clear();
    }

    private static string Rendered(string tool, Seen seen, long timestamp, int pulse) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"repeat #{seen.Count + 1} of this exact {tool} call {Seconds(timestamp - seen.Timestamp)}s ago - previous verdict: {seen.Verdict}; {Documents(pulse - seen.Pulse)}");

    private static long Seconds(long ticks) => Math.Max(0, ticks) / Stopwatch.Frequency;

    private static string Documents(int changed) =>
        changed > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{changed} document(s) changed since")
            : "nothing was written in between";

    internal static string Key(string tool, CallToolRequestParams parameters)
    {
        if (parameters.Arguments is not { Count: > 0 } arguments)
            return tool;

        var builder = new StringBuilder(tool);

        foreach (var argument in arguments.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            builder.Append(Separator).Append(argument.Key).Append('=').Append(argument.Value.GetRawText());

        return builder.ToString();
    }

    private const int MaxVerdict = 80;

    internal static string Verdict(CallToolResult result)
    {
        foreach (var block in result.Content)
        {
            if (block is TextContentBlock { Text.Length: > 0 } text)
                return Trimmed(text.Text);
        }

        return "unknown";
    }

    private static string Trimmed(string text)
    {
        var line = text.AsSpan();
        var end = line.IndexOf('\n');

        if (end >= 0)
            line = line[..end];

        line = line.TrimEnd();

        return new string(line.Length > MaxVerdict ? line[..MaxVerdict] : line);
    }

    private readonly record struct Seen(int Count, long Timestamp, int Pulse, string Verdict);

    private static readonly string Separator = new((char)31, 1);
}
