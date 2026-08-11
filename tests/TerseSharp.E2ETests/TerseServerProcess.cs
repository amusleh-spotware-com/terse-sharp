using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace TerseSharp.E2ETests;

internal sealed class TerseServerProcess
{
    private const int ShutdownTimeoutMilliseconds = 10_000;

    private readonly Process process;
    private readonly McpClient client;

    private TerseServerProcess(Process process, McpClient client)
    {
        this.process = process;
        this.client = client;
    }

    public McpClient Client => client;

    public static async Task<TerseServerProcess> StartAsync(
    string workingDirectory,
    IEnumerable<string> arguments,
    IReadOnlyDictionary<string, string> environment,
    CancellationToken cancellationToken)
    {
        var launched = arguments.ToArray();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            return await ConnectAsync(workingDirectory, launched, environment, cancellationToken);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return await ConnectAsync(workingDirectory, launched, environment, cancellationToken);
        }
        finally
        {
            E2ETelemetry.Started(stopwatch.ElapsedTicks);
        }
    }

    public async Task<string> CallAsync(string tool, Dictionary<string, object?> arguments, CancellationToken cancellationToken) =>
    ToolCensus.WithoutSteer(await CallRawAsync(tool, arguments, cancellationToken));

    public async Task<string> CallRawAsync(string tool, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = await client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken);

            return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        }
        finally
        {
            E2ETelemetry.Called(stopwatch.ElapsedTicks);
        }
    }

    public async ValueTask StopAsync()
    {
        await client.DisposeAsync();

        Terminate(process);
    }

    private static void Terminate(Process process)
    {
        CloseInput(process);

        if (!process.WaitForExit(ShutdownTimeoutMilliseconds))
            KillTree(process);

        process.Dispose();
    }

    private static Process Launch(
        string workingDirectory,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string> environment)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        start.Environment["TERSE_UPDATE"] = "0";

        foreach (var (name, value) in environment)
            start.Environment[name] = value;

        return Process.Start(start) ?? throw new InvalidOperationException("the terse server did not start");
    }

    private static void CloseInput(Process process)
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

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(ShutdownTimeoutMilliseconds);
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (Win32Exception)
        {
        }
    }
    private static async Task<TerseServerProcess> ConnectAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        var process = Launch(workingDirectory, arguments, environment);

        try
        {
            var transport = new StreamClientTransport(process.StandardInput.BaseStream, process.StandardOutput.BaseStream);
            var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

            return new TerseServerProcess(process, client);
        }
        catch
        {
            Terminate(process);

            throw;
        }
    }

    public static Task<TerseServerProcess> StartAsync(
        string workingDirectory,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken) =>
        StartAsync(workingDirectory, arguments, new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken);
}
