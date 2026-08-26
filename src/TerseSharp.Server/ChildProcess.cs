using System.Buffers;
using System.Diagnostics;
using System.Text;

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
            CancellationToken cancellationToken,
            IReadOnlyList<KeyValuePair<string, string>>? environment = null,
            System.Text.Encoding? encoding = null)
    {
        var start = StartInfo(fileName, arguments, workingDirectory, environment, encoding);
        var stopwatch = Stopwatch.StartNew();
        using var process = Started(start, fileName);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Detach(process);
        deadline.CancelAfter(timeout);

        var streams = Streaming(process);
        var run = await DrainAsync(process, stopwatch, streams, deadline.Token).ConfigureAwait(false);
        var finished = run ?? await AbandonAsync(process, stopwatch, streams, !cancellationToken.IsCancellationRequested).ConfigureAwait(false);

        return finished with { Command = Rendered(fileName, arguments) };
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

    private static async Task<ProcessRun?> DrainAsync(
        Process process,
        Stopwatch stopwatch,
        ProcessStreams streams,
        CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        stopwatch.Stop();

        return Completed(process.ExitCode, stopwatch.ElapsedMilliseconds, streams, await SettledAsync(process, streams).ConfigureAwait(false));
    }

    internal static ProcessStartInfo StartInfo(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyList<KeyValuePair<string, string>>? environment = null,
            System.Text.Encoding? encoding = null)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
        };

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        foreach (var variable in RegisteredMsBuildVariables)
            start.Environment.Remove(variable);

        foreach (var entry in environment ?? [])
            start.Environment[entry.Key] = entry.Value;

        return start;
    }

    private sealed class ProcessText
    {
        private readonly StringBuilder text = new();
        private readonly Lock gate = new();

        public int Length
        {
            get
            {
                lock (gate)
                    return text.Length;
            }
        }

        public void Append(char[] buffer, int count)
        {
            lock (gate)
                text.Append(buffer, 0, count);
        }

        public override string ToString()
        {
            lock (gate)
                return text.ToString();
        }
    }

    private readonly record struct ProcessStreams(Task Output, Task Error, ProcessText OutputText, ProcessText ErrorText);

    private const int CopyBufferLength = 4096;

    private static async Task CopyAsync(StreamReader reader, ProcessText text)
    {
        var buffer = ArrayPool<char>.Shared.Rent(CopyBufferLength);

        try
        {
            int read;

            while ((read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false)) > 0)
                text.Append(buffer, read);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static ProcessStreams Streaming(Process process)
    {
        var output = new ProcessText();
        var error = new ProcessText();

        return new ProcessStreams(
            CopyAsync(process.StandardOutput, output),
            CopyAsync(process.StandardError, error),
            output,
            error);
    }

    private static ProcessRun Completed(int exitCode, long elapsedMilliseconds, ProcessStreams streams, bool drained)
    {
        var output = streams.OutputText.ToString();
        var error = streams.ErrorText.ToString();

        return new ProcessRun(
            exitCode,
            output + error,
            elapsedMilliseconds,
            StandardOutput: output,
            StandardError: error,
            Drained: drained);
    }

    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(2);

    private static async Task<bool> SettledAsync(Process process, ProcessStreams streams)
    {
        try
        {
            await Task.WhenAll(streams.Output, streams.Error).WaitAsync(DrainGrace).ConfigureAwait(false);

            return true;
        }
        catch (Exception failure) when (failure is TimeoutException or IOException or ObjectDisposedException or OperationCanceledException)
        {
            await QuietAsync(streams).ConfigureAwait(false);

            Release(process);
            Observe(streams.Output);
            Observe(streams.Error);

            return false;
        }
    }

    private static string Break(string text) => text.Length is 0 || text.EndsWith('\n') ? string.Empty : "\n";

    private static string Stopped(string output, string error, long elapsedMilliseconds, bool timedOut) => string.Concat(
        output,
        error,
        Break(error.Length is 0 ? output : error),
        string.Create(
            CultureInfo.InvariantCulture,
            $"{(timedOut ? "TIMED_OUT" : "CANCELLED")} after {elapsedMilliseconds} ms; the process tree was killed"));

    private static async Task<ProcessRun> AbandonAsync(Process process, Stopwatch stopwatch, ProcessStreams streams, bool timedOut)
    {
        Stop(process);
        stopwatch.Stop();

        var drained = await SettledAsync(process, streams).ConfigureAwait(false);
        var output = streams.OutputText.ToString();
        var error = streams.ErrorText.ToString();

        return new ProcessRun(
            -1,
            Stopped(output, error, stopwatch.ElapsedMilliseconds, timedOut),
            stopwatch.ElapsedMilliseconds,
            timedOut,
            output,
            error,
            drained,
            Stopped: true);
    }

    private static void Observe(Task reader) =>
        reader.ContinueWith(static faulted => _ = faulted.Exception, TaskScheduler.Default);

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception failure) when (failure is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
        {
        }
    }

    private static void Release(Process process)
    {
        try
        {
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
        }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    internal static string Rendered(string fileName, IReadOnlyList<string> arguments) =>
            arguments.Count is 0 ? fileName : fileName + " " + string.Join(' ', arguments);

    private static readonly TimeSpan QuietStep = TimeSpan.FromMilliseconds(25);

    private static async Task QuietAsync(ProcessStreams streams)
    {
        var previous = -1;
        var settling = Stopwatch.StartNew();

        while (settling.Elapsed < SettleGrace)
        {
            var captured = streams.OutputText.Length + streams.ErrorText.Length;

            if (captured == previous)
                return;

            previous = captured;

            await Task.Delay(QuietStep).ConfigureAwait(false);
        }
    }

    private static readonly TimeSpan SettleGrace = TimeSpan.FromMilliseconds(250);
}
