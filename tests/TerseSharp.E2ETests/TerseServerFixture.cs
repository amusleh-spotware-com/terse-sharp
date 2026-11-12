using ModelContextProtocol.Client;

namespace TerseSharp.E2ETests;

public sealed class TerseServerFixture : IAsyncLifetime
{
    private TerseServerProcess? server;

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string FixtureRoot { get; } = Path.Combine(RepositoryRoot, "fixtures", "FixtureSolution");

    public McpClient Client => Server.Client;

    private TerseServerProcess Server => server ?? throw new InvalidOperationException("the client is not connected");

    public async ValueTask InitializeAsync()
    {
        baseline = await FixtureStatusAsync();
        server = await TerseServerProcess.StartAsync(
            FixtureRoot,
            [ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(FixtureRoot, "FixtureSolution.slnx")],
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (server is not null)
            await server.StopAsync();

        await AssertFixtureLeftCleanAsync();
    }

    public Task<string> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        Server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);

    public static string ServerAssemblyPath()
    {
        var configuration = Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)))!;
        var path = Path.Combine(RepositoryRoot, "src", "TerseSharp.Server", "bin", configuration, "net11.0", "terse.dll");

        return File.Exists(path) ? path : throw new FileNotFoundException("build TerseSharp.Server first", path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TerseSharp.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("TerseSharp.slnx not found above the test binaries");
    }

    public Task<string> CallRawAsync(string tool, Dictionary<string, object?> arguments) =>
        Server.CallRawAsync(tool, arguments, TestContext.Current.CancellationToken);

    private const int SettleAttempts = 20;
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(500);
    private HashSet<string> baseline = [];

    private static System.Diagnostics.ProcessStartInfo Git(string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        return start;
    }

    private static async Task<HashSet<string>> FixtureStatusAsync() =>
        [.. (await GitOutputAsync(["status", "--porcelain", "--untracked-files=all", "--", "fixtures/FixtureSolution"])).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private async Task<List<string>> IntroducedAsync() =>
        [.. (await FixtureStatusAsync()).Where(line => !baseline.Contains(line)).Order(StringComparer.Ordinal)];

    private async Task AssertFixtureLeftCleanAsync()
    {
        var introduced = await IntroducedAsync();

        for (var attempt = 0; introduced.Count > 0 && attempt < SettleAttempts; attempt++)
        {
            await Task.Delay(SettleDelay);
            introduced = await IntroducedAsync();
        }

        if (introduced.Count > 0)
            throw new InvalidOperationException("the shared fixture was left dirty by this collection: " + string.Join(", ", introduced) + "\n" + await FixtureDiffAsync());
    }

    private static async Task<string> GitOutputAsync(string[] arguments)
    {
        using var process = System.Diagnostics.Process.Start(Git(arguments)) ?? throw new InvalidOperationException("git did not start");

        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(output, error, process.WaitForExitAsync());

        return process.ExitCode is 0
            ? await output
            : throw new InvalidOperationException("git " + arguments[0] + " exited " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + ": " + await error);
    }

    private static async Task<string> FixtureDiffAsync()
    {
        var diff = await GitOutputAsync(["diff", "--", "fixtures/FixtureSolution"]);

        return diff.Length <= MaxReportedDiff ? diff : diff[..MaxReportedDiff];
    }

    private const int MaxReportedDiff = 4000;
}

[CollectionDefinition(nameof(TerseServerCollection))]
public sealed class TerseServerCollection : ICollectionFixture<TerseServerFixture>;
