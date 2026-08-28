using TerseSharp.Server;

namespace TerseSharp.E2ETests;

public sealed class ToolSettingsE2ETests : IAsyncLifetime
{
    private const string Prefix = "terse-settings-e2e";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory(Prefix);
    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync()
    {
        await File.WriteAllTextAsync(
            Path.Combine(root.FullName, ToolSettings.FileName),
            """{"tools":{"groups":{"xaml":false,"razor":false},"names":{"search_regex":false}}}""",
            TestContext.Current.CancellationToken);

        server = await TerseServerProcess.StartAsync(
            root.FullName,
            [
                TerseServerFixture.ServerAssemblyPath(),
                "serve",
                "--tools",
                "all",
                "--workspace",
                Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"),
            ],
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await server.StopAsync();

        root.Delete(recursive: true);
    }

    [Fact]
    public async Task ToolsList_WithASettingsFile_HidesTheDisabledGroupsAndTheDisabledName()
    {
        var advertised = await AdvertisedAsync();

        Assert.DoesNotContain("xaml_outline", advertised);
        Assert.DoesNotContain("razor_outline", advertised);
        Assert.DoesNotContain("search_regex", advertised);
        Assert.Contains("resx_get", advertised);
        Assert.Contains("search_text", advertised);
        Assert.Contains("get_file_outline", advertised);
    }

    [Fact]
    public async Task ToolsList_WithASettingsFile_CostsMeasurablyLessThanTheWholeSurface()
    {
        var narrowed = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var hidden = ToolCoverageE2ETests.ExercisedCount - narrowed.Count;
        var tokens = (narrowed.Sum(tool => tool.Name.Length + (tool.Description?.Length ?? 0) + tool.JsonSchema.GetRawText().Length) + 3) / 4;

        Assert.Equal(ToolGroups.All["xaml"].Length + ToolGroups.All["razor"].Length + 1, hidden);
        Assert.True(tokens <= 24200, string.Create(CultureInfo.InvariantCulture, $"the narrowed surface still costs {tokens} tokens over {narrowed.Count} tools"));
    }

    [Fact]
    public async Task AToolTheSettingsFileHides_IsAbsentFromTheListAndStillAnswersWhenCalledByName()
    {
        var text = await server.CallAsync(
            "search_regex",
            new() { ["query"] = "class\\s+OrderService", ["glob"] = "*.cs" },
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain("search_regex", await AdvertisedAsync());
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("OrderService.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceStatus_NamesTheSettingsFileByPathAndTheKeysItHid()
    {
        var text = await server.CallAsync("workspace_status", [], TestContext.Current.CancellationToken);

        Assert.Contains("tools=", text, StringComparison.Ordinal);
        Assert.Contains(Path.DirectorySeparatorChar + ToolSettings.FileName, text, StringComparison.Ordinal);
        Assert.Contains(Prefix, text, StringComparison.Ordinal);
        Assert.Contains("(xaml, razor, search_regex)", text, StringComparison.Ordinal);
        Assert.Contains("still answers when called by name", text, StringComparison.Ordinal);
    }

    private async Task<string[]> AdvertisedAsync() =>
        [.. (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).Select(tool => tool.Name)];
}
