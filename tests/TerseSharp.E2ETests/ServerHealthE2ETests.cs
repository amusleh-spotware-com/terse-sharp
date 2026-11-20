namespace TerseSharp.E2ETests;

public sealed class ServerHealthE2ETests : IAsyncLifetime
{
    private readonly string root = Directory.CreateTempSubdirectory("terse-e2e-health-").FullName;

    private readonly string home = Directory.CreateTempSubdirectory("terse-e2e-health-home-").FullName;

    private TerseServerProcess server = null!;

    private string CrashLogPath => Path.Combine(home, ".terse", "crash.log");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(home, ".terse"));
        await File.WriteAllTextAsync(CrashLogPath, "2026-11-20T12:00:00.0000000Z pid=1 terse=0.0.0 terminating=True workingSetMB=1\nSystem.OutOfMemoryException: probe\n\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "Probe.slnx"), "<Solution>\n  <Project Path=\"Probe.csproj\" />\n</Solution>\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net11.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "Lonely.cs"), "namespace Probe;\n\npublic sealed class Lonely;\n", TestContext.Current.CancellationToken);

        server = await TerseServerProcess.StartAsync(
            root,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(root, "Probe.slnx")],
            new Dictionary<string, string> { ["TERSE_UPDATE"] = "0", ["TERSE_HOME"] = home },
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await server.StopAsync();
        TerseTempSolution.Delete(root);
        Directory.Delete(home, recursive: true);
    }

    [Fact]
    public async Task WorkspaceStatus_WithACrashRecordUnderTheHome_NamesItAndTheFileToRead()
    {
        var text = await CallAsync("workspace_status", []);

        Assert.Contains("WARNING terse crashed at ", text, StringComparison.Ordinal);
        Assert.Contains("read_text " + Path.GetFullPath(CrashLogPath) + " for the exception", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceStatusVerbose_AfterAnUnloadAndReload_CountsNoRetiredWorkspaceStillAlive()
    {
        var solution = Path.Combine(root, "Probe.slnx");

        await CallAsync("unload_workspace", new() { ["path"] = solution });
        await CallAsync("load_workspace", new() { ["path"] = solution });

        var text = await CallAsync("workspace_status", new() { ["verbose"] = true });

        Assert.Contains("OK   retired: retired=0 alive", text, StringComparison.Ordinal);
    }

    private Task<string> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);
}
