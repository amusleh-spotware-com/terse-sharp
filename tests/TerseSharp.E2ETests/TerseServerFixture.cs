using ModelContextProtocol.Client;

namespace TerseSharp.E2ETests;

public sealed class TerseServerFixture : IAsyncLifetime
{
    private TerseServerProcess? server;

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string FixtureRoot { get; } = Path.Combine(RepositoryRoot, "fixtures", "FixtureSolution");

    public McpClient Client => Server.Client;

    private TerseServerProcess Server => server ?? throw new InvalidOperationException("the client is not connected");

    public async ValueTask InitializeAsync() => server = await TerseServerProcess.StartAsync(
        FixtureRoot,
        [ServerAssemblyPath(), "serve", "--workspace", Path.Combine(FixtureRoot, "FixtureSolution.slnx")],
        TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (server is not null)
            await server.StopAsync();
    }

    public Task<string> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        Server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);

    public static string ServerAssemblyPath()
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
