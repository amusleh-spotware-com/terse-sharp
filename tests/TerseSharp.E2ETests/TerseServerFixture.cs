using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace TerseSharp.E2ETests;

public sealed class TerseServerFixture : IAsyncLifetime
{
    private const int ShutdownTimeoutMilliseconds = 10_000;

    private Process? server;
    private McpClient? client;

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string FixtureRoot { get; } = Path.Combine(RepositoryRoot, "fixtures", "FixtureSolution");

    public McpClient Client => client ?? throw new InvalidOperationException("the client is not connected");

    public async ValueTask InitializeAsync()
    {
        server = StartServer();

        var transport = new StreamClientTransport(server.StandardInput.BaseStream, server.StandardOutput.BaseStream);

        client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (client is not null)
            await client.DisposeAsync();

        Terminate(server);
    }

    public async Task<string> CallAsync(string tool, Dictionary<string, object?> arguments)
    {
        var result = await Client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken);

        return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }

    public static string ServerAssemblyPath()
    {
        var configuration = Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)))!;
        var path = Path.Combine(RepositoryRoot, "src", "TerseSharp.Server", "bin", configuration, "net10.0", "terse.dll");

        return File.Exists(path) ? path : throw new FileNotFoundException("build TerseSharp.Server first", path);
    }

    private static Process StartServer()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = FixtureRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        foreach (var argument in ServerArguments())
            start.ArgumentList.Add(argument);

        return Process.Start(start) ?? throw new InvalidOperationException("the terse server did not start");
    }

    private static string[] ServerArguments() =>
        [ServerAssemblyPath(), "serve", "--workspace", Path.Combine(FixtureRoot, "FixtureSolution.slnx")];

    private static void Terminate(Process? server)
    {
        if (server is null)
            return;

        CloseInput(server);

        if (!server.WaitForExit(ShutdownTimeoutMilliseconds))
            KillTree(server);

        server.Dispose();
    }

    private static void CloseInput(Process server)
    {
        try
        {
            server.StandardInput.Close();
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void KillTree(Process server)
    {
        try
        {
            server.Kill(entireProcessTree: true);
            server.WaitForExit(ShutdownTimeoutMilliseconds);
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TerseSharp.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("TerseSharp.slnx not found above the test binaries");
    }
}

[CollectionDefinition(nameof(TerseServerCollection))]
public sealed class TerseServerCollection : ICollectionFixture<TerseServerFixture>;
