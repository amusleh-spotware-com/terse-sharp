using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace TerseSharp.Server;

internal static partial class LockHolders
{
    private const int MaxHolders = 8;
    private const int MaxTail = 160;

    public static async Task<string> DescribeAsync(string output, string root = "", CancellationToken cancellationToken = default)
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

                builder.Append("\nholder pid=").Append(pid.ToString(CultureInfo.InvariantCulture)).Append(' ')
                    .Append(await ResolvedAsync(pid, root, cancellationToken).ConfigureAwait(false));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return builder.ToString();
        }

        return builder.ToString();
    }

    private static async Task<string> ResolvedAsync(int pid, string root, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var command = await CommandTailAsync(pid, root, cancellationToken).ConfigureAwait(false);

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{process.ProcessName} startedUtc={Started(process)} age={Age(process)}{Executable(process, root)}{command} - {Kind(process.Id, process.ProcessName, root)}");
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

    private static string Age(Process process)
    {
        try
        {
            return Aged(DateTime.UtcNow - process.StartTime.ToUniversalTime());
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "unknown";
        }
    }

    private static string Aged(TimeSpan elapsed) => elapsed switch
    {
        { TotalHours: >= 1 } => elapsed.TotalHours.ToString("0.0", CultureInfo.InvariantCulture) + "h",
        { TotalMinutes: >= 1 } => elapsed.TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture) + "m",
        _ => Math.Max(elapsed.TotalSeconds, 0).ToString("0", CultureInfo.InvariantCulture) + "s",
    };

    private static async Task<string> CommandTailAsync(int pid, string root, CancellationToken cancellationToken)
    {
        var line = await RawCommandLineAsync(pid, cancellationToken).ConfigureAwait(false);

        return line is { Length: > 0 } ? " cmd=" + Tail(line, root) : string.Empty;
    }

    private static async Task<string?> RawCommandLineAsync(int pid, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
            return WindowsCommandLine(pid);

        if (OperatingSystem.IsLinux())
            return await LinuxCommandLineAsync(pid, cancellationToken).ConfigureAwait(false);

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string? WindowsCommandLine(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(string.Create(CultureInfo.InvariantCulture, $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}"));
            using var results = searcher.Get();

            foreach (var item in results)
            {
                using (item)
                    return item["CommandLine"] as string;
            }
        }
        catch (ManagementException)
        {
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }

        return null;
    }

    private static async Task<string?> LinuxCommandLineAsync(int pid, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await File.ReadAllTextAsync(string.Create(CultureInfo.InvariantCulture, $"/proc/{pid}/cmdline"), cancellationToken).ConfigureAwait(false);

            return raw.Replace('\0', ' ').TrimEnd();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Tail(string commandLine, string root)
    {
        var trimmed = root.Length > 0
            ? commandLine.Replace(root + Path.DirectorySeparatorChar, "", StringComparison.OrdinalIgnoreCase)
            : commandLine;
        var span = trimmed.AsSpan();
        var marker = span.IndexOf(".dll", StringComparison.OrdinalIgnoreCase);

        if (marker < 0)
            marker = span.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);

        var start = marker < 0 ? 0 : span[..marker].LastIndexOfAny('\\', '/') + 1;
        var tail = span[start..].Trim();

        return tail.Length > MaxTail ? string.Concat(tail[..MaxTail], "...") : new string(tail);
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

    public static async Task<string> ScannedAsync(string root, CancellationToken cancellationToken = default)
    {
        if (root.Length is 0)
            return string.Empty;

        var builder = new StringBuilder();
        var current = Environment.ProcessId;
        var found = await SelfAsync(builder, root, cancellationToken).ConfigureAwait(false);

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (found >= MaxHolders || process.Id == current || Mapped(process, root) is not { } module)
                    continue;

                found++;
                await AppendAsync(builder, process, root, module, cancellationToken).ConfigureAwait(false);
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

    private static async Task<int> SelfAsync(StringBuilder builder, string root, CancellationToken cancellationToken)
    {
        using var current = Process.GetCurrentProcess();

        if (Mapped(current, root) is not { } module)
            return 0;

        await AppendAsync(builder, current, root, module, cancellationToken).ConfigureAwait(false);

        return 1;
    }

    private static async Task AppendAsync(StringBuilder builder, Process process, string root, string module, CancellationToken cancellationToken)
    {
        var command = await CommandTailAsync(process.Id, root, cancellationToken).ConfigureAwait(false);

        builder.Append("\nholder pid=").Append(process.Id.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(process.ProcessName).Append(" startedUtc=").Append(Started(process)).Append(" age=").Append(Age(process)).Append(Executable(process, root))
            .Append(command).Append(" maps=").Append(module).Append(" - ").Append(Kind(process.Id, process.ProcessName, root));
    }
}
