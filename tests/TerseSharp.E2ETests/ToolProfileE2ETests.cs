using TerseSharp.Server;

namespace TerseSharp.E2ETests;

public sealed class ToolProfileE2ETests : IAsyncLifetime
{
    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync() => server = await TerseServerProcess.StartAsync(
        TerseServerFixture.FixtureRoot,
        [
            TerseServerFixture.ServerAssemblyPath(),
            "serve",
            "--tools",
            "core",
            "--workspace",
            Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"),
        ],
        TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await server.StopAsync();

    [Fact]
    public async Task ToolsList_WithCoreRequested_AdvertisesExactlyTheCoreProfile()
    {
        var advertised = (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(ToolProfile.CoreTools.Count, advertised.Count);
        Assert.All(ToolProfile.CoreTools, name => Assert.Contains(name, advertised));
        Assert.DoesNotContain("xaml_outline", advertised);
        Assert.DoesNotContain("resx_get", advertised);
    }

    [Fact]
    public async Task AToolTheProfileHides_StillAnswersWhenCalledByName()
    {
        var text = await CallAsync("get_symbol", new() { ["symbolId"] = "OrderService.Submit" });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("Submit", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceStatus_SaysWhichProfileIsRunningAndThatTheRestStillAnswer()
    {
        var text = await CallAsync("workspace_status", []);

        Assert.Contains("tools=core", text, StringComparison.Ordinal);
        Assert.Contains("still answers when called by name", text, StringComparison.Ordinal);
    }

    private Task<string> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);

    [Fact]
    public async Task WorkspaceStatusVerbose_ReportsTheWholeSurfaceBesideTheNarrowedOne()
    {
        await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var text = await CallAsync("workspace_status", new() { ["verbose"] = true });
        var line = Array.Find(text.Split('\n'), entry => entry.StartsWith("advertised=", StringComparison.Ordinal));

        Assert.NotNull(line);

        var parts = line.Split(' ');

        Assert.Equal("of", parts[4]);
        Assert.Equal(ToolProfile.CoreTools.Count, int.Parse(parts[0]["advertised=".Length..], CultureInfo.InvariantCulture));
        Assert.True(int.Parse(parts[5], CultureInfo.InvariantCulture) > ToolProfile.CoreTools.Count, line);
        Assert.True(int.Parse(parts[7], CultureInfo.InvariantCulture) > int.Parse(parts[2], CultureInfo.InvariantCulture), line);
    }
}
