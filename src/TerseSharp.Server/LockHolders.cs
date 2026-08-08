using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace TerseSharp.Server;

internal static partial class LockHolders
{
    private const int MaxHolders = 8;

    public static string Describe(string output)
    {
        var seen = new HashSet<int>();
        var builder = new StringBuilder();

        try
        {
            foreach (Match match in Holder().Matches(output))
            {
                if (!int.TryParse(match.Groups[2].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
                    continue;

                if (seen.Count >= MaxHolders || !seen.Add(pid))
                    continue;

                builder.Append("\nholder pid=").Append(pid.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Resolved(pid));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return builder.ToString();
        }

        return builder.ToString();
    }

    private static string Resolved(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{process.ProcessName} startedUtc={Started(process)} - {Kind(process.Id, process.ProcessName)}");
        }
        catch (ArgumentException)
        {
            return "already gone - the lock it held is released; retry";
        }
        catch (InvalidOperationException)
        {
            return "already gone - the lock it held is released; retry";
        }
    }

    private static string Started(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "unknown";
        }
    }

    private static string Kind(int pid, string name)
    {
        if (pid == Environment.ProcessId)
            return "this terse server";

        if (name.Contains("BuildHost", StringComparison.OrdinalIgnoreCase) || name.Contains("MSBuild", StringComparison.OrdinalIgnoreCase))
            return "an MSBuild host, most likely spawned out of this tree's own bin/ by an earlier terse load; stopping it is safe once no build is running";

        if (name.Contains("testhost", StringComparison.OrdinalIgnoreCase) || name.Contains("vstest", StringComparison.OrdinalIgnoreCase))
            return "a live test run; wait for it rather than stopping it";

        return name.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? "a dotnet host; read its start time before stopping it - it may be another session's build or test"
            : "not a process this server recognises; read its command line before stopping it";
    }

    [GeneratedRegex("\"([^\"()]+?)\\s*\\((\\d+)\\)\"", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Holder();
}
