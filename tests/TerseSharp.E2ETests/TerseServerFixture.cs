using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace TerseSharp.E2ETests;

public sealed class TerseServerFixture : IAsyncLifetime
{
    private McpClient? client;

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string FixtureRoot { get; } = Path.Combine(RepositoryRoot, "fixtures", "FixtureSolution");

    public McpClient Client => client ?? throw new InvalidOperationException("the client is not connected");

    public async ValueTask InitializeAsync()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "terse-sharp",
            Command = "dotnet",
            Arguments = [ServerAssemblyPath(), "serve", "--workspace", Path.Combine(FixtureRoot, "FixtureSolution.slnx")],
            WorkingDirectory = FixtureRoot,
        });

        client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (client is not null)
            await client.DisposeAsync();
    }

    public async Task<string> CallAsync(string tool, Dictionary<string, object?> arguments)
    {
        var result = await Client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken);

        return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }

    private static string ServerAssemblyPath()
    {
        var configuration = Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)))!;
        var path = Path.Combine(RepositoryRoot, "src", "TerseSharp.Server", "bin", configuration, "net10.0", "terse.dll");

        return File.Exists(path) ? path : throw new FileNotFoundException("build TerseSharp.Server first", path);
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
