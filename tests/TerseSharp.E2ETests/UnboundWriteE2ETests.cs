namespace TerseSharp.E2ETests;

public sealed class UnboundWriteE2ETests : IAsyncLifetime
{
    private readonly string home = Directory.CreateTempSubdirectory("terse-e2e-unbound-").FullName;

    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(home, ".git"));

        server = await TerseServerProcess.StartAsync(
            home,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all"],
            new Dictionary<string, string> { ["TERSE_UPDATE"] = "0", ["TERSE_HOME"] = home },
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await server.StopAsync();
        Directory.Delete(home, recursive: true);
    }

    [Fact]
    public async Task WriteText_WithNoWorkspaceLoaded_LandsAForcedWriteOutsideEveryRootAsReadTextReadsOne()
    {
        var target = Path.Combine(home, "notes", "unbound.txt");

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var status = await server.CallAsync("list_workspaces", [], TestContext.Current.CancellationToken);
        var refused = await server.CallAsync("write_text", new() { ["path"] = target, ["content"] = "one\n" }, TestContext.Current.CancellationToken);
        var written = await server.CallAsync("write_text", new() { ["path"] = target, ["content"] = "one\n", ["force"] = true }, TestContext.Current.CancellationToken);
        var read = await server.CallAsync("read_text", new() { ["path"] = target }, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(".slnx", status, StringComparison.Ordinal);
        Assert.StartsWith("ERROR OutOfWorkspace", refused, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkspaceNotLoaded", written, StringComparison.Ordinal);
        Assert.Contains("outside-workspace", written, StringComparison.Ordinal);
        Assert.Equal("one\n", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        Assert.Contains("one", read, StringComparison.Ordinal);
    }
}
