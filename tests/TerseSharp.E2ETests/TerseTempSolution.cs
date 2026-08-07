namespace TerseSharp.E2ETests;

internal sealed class TerseTempSolution : IAsyncDisposable
{
    private readonly TerseServerProcess server;

    private TerseTempSolution(TerseServerProcess server, string root)
    {
        this.server = server;
        Root = root;
    }

    public string Root { get; }

    public string SolutionPath => Path.Combine(Root, "FixtureSolution.slnx");

    public string ProjectDirectory => Path.Combine(Root, "src", "Fixture.Trading");

    public string ProjectPath => Path.Combine(ProjectDirectory, "Fixture.Trading.csproj");

    public string OrderServicePath => Path.Combine(ProjectDirectory, "OrderService.cs");

    public static async Task<TerseTempSolution> StartAsync(bool watch, CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), "terse-e2e-sync-" + Guid.NewGuid().ToString("N"));

        Copy(Path.Combine(TerseServerFixture.FixtureRoot), root);

        var server = await TerseServerProcess.StartAsync(root, Arguments(root, watch), cancellationToken);

        await server.CallAsync("workspace_status", [], cancellationToken);

        return new TerseTempSolution(server, root);
    }

    public Task<string> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            foreach (var extra in attached)
                await extra.StopAsync();
        }
        finally
        {
            await server.StopAsync();
            Delete(Root);
        }
    }
    private static string[] Arguments(string root, bool watch)
    {
        string[] common =
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--workspace", Path.Combine(root, "FixtureSolution.slnx")];

        return watch ? common : [.. common, "--no-watch"];
    }

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
            CopyFile(file, Path.Combine(destination, Path.GetFileName(file)));

        foreach (var directory in Directory.EnumerateDirectories(source).Where(Copyable))
            Copy(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void CopyFile(string file, string destination)
    {
        try
        {
            File.Copy(file, destination);
        }
        catch (FileNotFoundException)
        {
        }
    }

    private static bool Copyable(string directory) => Path.GetFileName(directory) is not ("bin" or "obj");

    private static void Delete(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private readonly List<TerseServerProcess> attached = [];

    public async Task<TerseServerProcess> AttachAsync(bool watch, CancellationToken cancellationToken)
    {
        var second = await TerseServerProcess.StartAsync(Root, Arguments(Root, watch), cancellationToken);
        await second.CallAsync("workspace_status", [], cancellationToken);
        attached.Add(second);
        return second;
    }
}
