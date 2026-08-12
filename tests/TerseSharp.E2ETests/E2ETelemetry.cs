using System.Diagnostics;

namespace TerseSharp.E2ETests;

internal static class E2ETelemetry
{
    private static long starts;
    private static long handshakeTicks;
    private static long calls;
    private static long callTicks;

    static E2ETelemetry() => AppDomain.CurrentDomain.ProcessExit += (_, _) => Emit(Report());

    private static void Emit(string report)
    {
        var directory = Environment.GetEnvironmentVariable("TERSE_RESULTS_DIRECTORY");

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            Console.Error.WriteLine(report);

            return;
        }

        var name = "terse-notes-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".txt";

        try
        {
            File.WriteAllText(Path.Combine(directory, name), report);
        }
        catch (IOException)
        {
            Console.Error.WriteLine(report);
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine(report);
        }
    }

    public static long Starts => Volatile.Read(ref starts);

    public static long Calls => Volatile.Read(ref calls);

    public static void Started(long ticks)
    {
        Interlocked.Increment(ref starts);
        Interlocked.Add(ref handshakeTicks, ticks);
    }

    public static void Called(long ticks)
    {
        Interlocked.Increment(ref calls);
        Interlocked.Add(ref callTicks, ticks);
    }

    public static string Report() => string.Create(
        CultureInfo.InvariantCulture,
        $"e2e attribution: starts={Starts} startMs={Milliseconds(Volatile.Read(ref handshakeTicks))} calls={Calls} callMs={Milliseconds(Volatile.Read(ref callTicks))}");

    private static long Milliseconds(long ticks) => ticks * 1000 / Stopwatch.Frequency;
}
