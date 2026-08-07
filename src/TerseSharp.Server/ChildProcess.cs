using System.Diagnostics;

namespace TerseSharp.Server;

internal static class ChildProcess
{
    private static readonly string[] RegisteredMsBuildVariables =
        ["MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath"];

    public static async Task<ProcessRun> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var start = StartInfo(fileName, arguments, workingDirectory);
        var stopwatch = Stopwatch.StartNew();
        using var process = Started(start, fileName);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Detach(process);
        deadline.CancelAfter(timeout);

        var run = await DrainAsync(process, stopwatch, deadline.Token).ConfigureAwait(false);

        return run ?? Abandon(process, stopwatch);
    }

    private static void Detach(Process process)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static Process Started(ProcessStartInfo start, string fileName)
    {
        try
        {
            return Process.Start(start) ?? throw new InvalidOperationException(fileName + " did not start");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(fileName + " could not be started: " + exception.Message, exception);
        }
    }

    private static async Task<ProcessRun?> DrainAsync(Process process, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var pending = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            var output = await pending.ConfigureAwait(false);
            var error = await errors.ConfigureAwait(false);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            return new ProcessRun(
                process.ExitCode,
                output + error,
                stopwatch.ElapsedMilliseconds,
                StandardOutput: output,
                StandardError: error);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static ProcessRun Abandon(Process process, Stopwatch stopwatch)
    {
        stopwatch.Stop();

        if (!process.HasExited)
            process.Kill(entireProcessTree: true);

        return new ProcessRun(
            -1,
            string.Create(CultureInfo.InvariantCulture, $"TIMED_OUT after {stopwatch.ElapsedMilliseconds} ms; the process tree was killed"),
            stopwatch.ElapsedMilliseconds,
            TimedOut: true);
    }

    internal static ProcessStartInfo StartInfo(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        foreach (var variable in RegisteredMsBuildVariables)
            start.Environment.Remove(variable);

        return start;
    }
}
