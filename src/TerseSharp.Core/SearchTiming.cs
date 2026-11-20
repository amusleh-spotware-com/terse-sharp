using System.Diagnostics;

namespace TerseSharp.Core;

public readonly record struct SearchTiming(TimeSpan Index, TimeSpan Scan, int Files)
{
    public TimeSpan Total => Index + Scan;

    public static SearchTiming Since(long started, long chosen, int files) =>
        new(Stopwatch.GetElapsedTime(started, chosen), Stopwatch.GetElapsedTime(chosen), files);

    public string Appended(string response, TimeSpan slowAfter) => Total > slowAfter
        ? string.Create(CultureInfo.InvariantCulture, $"{response}\ntiming index={WholeMilliseconds(Index)} scan={WholeMilliseconds(Scan)} files={Files}")
        : response;

    private static long WholeMilliseconds(TimeSpan span) => (long)span.TotalMilliseconds;
}
