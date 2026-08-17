using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace TerseSharp.Server;

internal static partial class LockHolders
{
    private const int MaxHolders = 8;

    public static string Describe(string output, string root = "")
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

                builder.Append("\nholder pid=").Append(pid.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Resolved(pid, root));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return builder.ToString();
        }

        return builder.ToString();
    }

    private static string Resolved(int pid, string root)
    {
        try
        {
            using var process = Process.GetProcessById(pid);

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{process.ProcessName} startedUtc={Started(process)}{Executable(process, root)} - {Kind(process.Id, process.ProcessName)}");
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
            : "not a process this server recognises; the exe above says whether it is running out of this tree's own output";
    }

    [GeneratedRegex("\"([^\"()]+?)\\s*\\((\\d+)\\)\"", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Holder();

    private static string Executable(Process process, string root)
    {
        try
        {
            return process.MainModule?.FileName is { Length: > 0 } path ? " exe=" + Relative(path, root) : string.Empty;
        }
        catch (Exception failure) when (failure is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return string.Empty;
        }
    }

    private static string Relative(string path, string root) =>
        root.Length > 0 && PathBoundary.Contains(root, path) ? Path.GetRelativePath(root, path) : path;
}
