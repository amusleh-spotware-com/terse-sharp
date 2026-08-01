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
        CancellationToken cancellationToken)
    {
        var launched = arguments.ToArray();

        try
        {
            return await ConnectAsync(workingDirectory, launched, cancellationToken);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return await ConnectAsync(workingDirectory, launched, cancellationToken);
        }
    }

    public async Task<string> CallAsync(string tool, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken);

        return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
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

    private static Process Launch(string workingDirectory, IEnumerable<string> arguments)
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
        CancellationToken cancellationToken)
    {
        var process = Launch(workingDirectory, arguments);

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
}
