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
                $"{process.ProcessName} startedUtc={Started(process)}{Executable(process, root)} - {Kind(process.Id, process.ProcessName, root)}");
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

    private static string Kind(int pid, string name, string root)
    {
        if (pid == Environment.ProcessId)
            return "this terse server";

        if (name.Contains("BuildHost", StringComparison.OrdinalIgnoreCase) || name.Contains("MSBuild", StringComparison.OrdinalIgnoreCase))
            return "an MSBuild host, most likely spawned out of this tree's own bin/ by an earlier terse load; stopping it is safe once no build is running";

        if (name.Contains("testhost", StringComparison.OrdinalIgnoreCase) || name.Contains("vstest", StringComparison.OrdinalIgnoreCase))
            return "a live test run; wait for it rather than stopping it";

        if (!name.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return "not a process this server recognises; the exe above says whether it is running out of this tree's own output";

        return LiveTestRun(root) is { } host
            ? "a dotnet host, and " + host + " is running out of this same tree - HEURISTIC, but this holder is almost certainly part of that test run, so wait for it rather than stopping it"
            : "a dotnet host, and no test host of this tree is running; read its start time before stopping it - it may be another session's build";
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

    public static string? LiveTestRun(string root)
    {
        if (root.Length is 0)
            return null;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (Hosted(process, root) is { } named)
                    return named;
            }
        }

        return null;
    }

    private static string? Hosted(Process process, string root) =>
        Located(process) is { Length: > 0 } path
        && Path.GetFileNameWithoutExtension(path.AsSpan()).Contains("test", StringComparison.OrdinalIgnoreCase)
        && PathBoundary.Contains(root, path)
            ? string.Create(CultureInfo.InvariantCulture, $"{process.ProcessName} pid={process.Id}")
            : null;

    private static string? Located(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception failure) when (failure is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    public static string Scanned(string root)
    {
        if (root.Length is 0)
            return string.Empty;

        var builder = new StringBuilder();
        var current = Environment.ProcessId;
        var found = Self(builder, root);

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (found >= MaxHolders || process.Id == current || Mapped(process, root) is not { } module)
                    continue;

                found++;
                Append(builder, process, root, module);
            }
        }

        return builder.ToString();
    }

    private static string? Mapped(Process process, string root)
    {
        if (!IsRunner(process.ProcessName))
            return null;

        try
        {
            foreach (ProcessModule module in process.Modules)
            {
                using (module)
                {
                    if (module.FileName is { Length: > 0 } path && PathBoundary.Contains(root, path))
                        return Relative(path, root);
                }
            }
        }
        catch (Exception failure) when (failure is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return null;
        }

        return null;
    }

    private static bool IsRunner(string name) =>
        name.Contains("dotnet", StringComparison.OrdinalIgnoreCase)
        || name.Contains("test", StringComparison.OrdinalIgnoreCase)
        || name.Contains("MSBuild", StringComparison.OrdinalIgnoreCase)
        || name.Contains("BuildHost", StringComparison.OrdinalIgnoreCase)
        || name.Contains("terse", StringComparison.OrdinalIgnoreCase);

    private static int Self(StringBuilder builder, string root)
    {
        using var current = Process.GetCurrentProcess();

        if (Mapped(current, root) is not { } module)
            return 0;

        Append(builder, current, root, module);

        return 1;
    }

    private static void Append(StringBuilder builder, Process process, string root, string module) =>
        builder.Append("\nholder pid=").Append(process.Id.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(process.ProcessName).Append(" startedUtc=").Append(Started(process)).Append(Executable(process, root))
            .Append(" maps=").Append(module).Append(" - ").Append(Kind(process.Id, process.ProcessName, root));
}
