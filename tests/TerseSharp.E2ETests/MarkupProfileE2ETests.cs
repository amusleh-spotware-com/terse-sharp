using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using TerseSharp.Server;

namespace TerseSharp.E2ETests;

public sealed class MarkupProfileE2ETests : IAsyncLifetime
{
    private static readonly string Root =
        Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "SelectionSolution");

    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync() => server = await TerseServerProcess.StartAsync(
        Root,
        [
            TerseServerFixture.ServerAssemblyPath(),
            "serve",
            "--workspace",
            Path.Combine(Root, "SelectionSolution.slnx"),
        ],
        new Dictionary<string, string> { ["TERSE_TOOLS"] = string.Empty },
        TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await server.StopAsync();

    [Fact]
    public async Task ToolsList_OverASolutionWithNoMarkup_HidesEveryXamlRazorAndResxTool()
    {
        var advertised = await AdvertisedAsync();
        var gated = advertised.Where(name => Prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();

        Assert.True(gated.Length is 0, "families this workspace cannot serve are still advertised: " + string.Join(", ", gated));
        Assert.Contains("get_file_outline", advertised);
        Assert.Contains("clean", advertised);
    }

    [Fact]
    public async Task ToolsList_OverASolutionWithNoMarkup_CostsMeasurablyLessThanTheWholeSurface()
    {
        var narrowed = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var hidden = ToolCoverageE2ETests.ExercisedCount - narrowed.Count;
        var tokens = (narrowed.Sum(tool => tool.Name.Length + (tool.Description?.Length ?? 0) + tool.JsonSchema.GetRawText().Length) + 3) / 4;

        Assert.True(hidden >= 30, string.Create(CultureInfo.InvariantCulture, $"only {hidden} tools were hidden"));
        Assert.True(tokens <= 22350, string.Create(CultureInfo.InvariantCulture, $"the narrowed surface still costs {tokens} tokens over {narrowed.Count} tools"));
    }

    [Fact]
    public async Task AToolTheWorkspaceCannotServe_StillAnswersWhenCalledByName()
    {
        var text = await server.CallAsync("xaml_validate", [], TestContext.Current.CancellationToken);

        Assert.DoesNotContain("ERROR Internal", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Unknown tool", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceStatus_NamesEveryFamilyTheWorkspaceCannotServe()
    {
        await OnlySelectionAsync();

        var alone = await server.CallAsync("workspace_status", [], TestContext.Current.CancellationToken);

        await LoadFixtureAsync();

        var beside = await server.CallAsync(
            "workspace_status",
            new() { ["workspace"] = "SelectionSolution" },
            TestContext.Current.CancellationToken);

        await OnlySelectionAsync();

        Assert.Contains("xaml_*, razor_*, resx_* hidden", alone, StringComparison.Ordinal);
        Assert.Contains("still answers when called by name", alone, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", beside, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryToolTheGuardNamesForAFileThisWorkspaceHolds_IsAdvertised()
    {
        var advertised = await AdvertisedAsync();
        var named = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var path in new[] { "src/Selection.Core/Adder.cs", "src/Selection.Core/Selection.Core.csproj" })
        {
            var verdict = ToolGuard.Inspect("Read", new JsonObject { ["file_path"] = path });

            Assert.True(verdict.Denied, verdict.Reason);

            foreach (var tool in advertised)
            {
                if ((verdict.Reason + " " + verdict.Routing).Contains(tool, StringComparison.Ordinal))
                    named.Add(tool);
            }
        }

        Assert.NotEmpty(named);
        Assert.All(named, name => Assert.Contains(name, advertised));
    }

    private static string[] Prefixes { get; } = ["xaml_", "razor_", "resx_"];

    private async Task<string[]> AdvertisedAsync() =>
        [.. (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).Select(tool => tool.Name)];

    [Fact]
    public async Task LoadingASecondWorkspaceThatHoldsMarkup_ReAdvertisesTheFamiliesItServes()
    {
        await OnlySelectionAsync();

        var before = await AdvertisedAsync();
        var announced = 0;

        await using var subscription = server.Client.RegisterNotificationHandler(
            NotificationMethods.ToolListChangedNotification,
            (_, _) =>
            {
                Interlocked.Increment(ref announced);

                return ValueTask.CompletedTask;
            });

        var loaded = await LoadFixtureAsync();
        var after = await AdvertisedAsync();

        await OnlySelectionAsync();

        Assert.DoesNotContain("ERROR", loaded, StringComparison.Ordinal);
        Assert.DoesNotContain("xaml_outline", before);
        Assert.Contains("xaml_outline", after);
        Assert.Contains("razor_outline", after);
        Assert.Contains("resx_get", after);
        Assert.Equal(ToolCoverageE2ETests.ExercisedCount, after.Length);
        Assert.True(announced > 0, "the server never sent notifications/tools/list_changed");
    }

    private Task<string> LoadFixtureAsync() => server.CallAsync(
        "load_workspace",
        new() { ["path"] = Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx") },
        TestContext.Current.CancellationToken);

    private async Task OnlySelectionAsync() => await server.CallAsync(
        "unload_workspace",
        new() { ["path"] = Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx") },
        TestContext.Current.CancellationToken);
}
