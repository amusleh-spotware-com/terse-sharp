using System.Diagnostics;

namespace TerseSharp.Server;

internal readonly record struct ServerProcess(int Pid, DateTime StartedUtc);

internal static class LingeringServers
{
    private const int MaxShown = 5;

    private static readonly TimeSpan Threshold = TimeSpan.FromHours(24);

    public static string? Warning() => Describe(Others(), DateTime.UtcNow);

    internal static string? Describe(IReadOnlyList<ServerProcess> others, DateTime now)
    {
        ServerProcess[] lingering = [.. others.Where(server => now - server.StartedUtc > Threshold).OrderBy(server => server.StartedUtc)];

        return lingering.Length is 0
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"WARNING {lingering.Length} other terse server(s) running for over a day: {string.Join(", ", lingering.Take(MaxShown).Select(server => Sketch(server, now)))} - one whose session has ended can hold built binaries, so a build no-ops and a test run reports the previous binary's result; stop it");
    }

    private static string Sketch(ServerProcess server, DateTime now) =>
        string.Create(CultureInfo.InvariantCulture, $"pid={server.Pid} age={Age(now - server.StartedUtc)}");

    private static string Age(TimeSpan age) => age.TotalDays >= 2
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalDays}d")
        : string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalHours}h");

    private static ServerProcess[] Others()
    {
        var found = new List<ServerProcess>();

        foreach (var process in Process.GetProcessesByName("terse"))
        {
            using (process)
            {
                if (process.Id != Environment.ProcessId && Started(process) is { } started)
                    found.Add(new(process.Id, started));
            }
        }

        return [.. found];
    }

    private static DateTime? Started(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }
}
