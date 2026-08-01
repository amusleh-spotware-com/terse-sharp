
namespace TerseSharp.E2ETests;

public sealed class WorkspaceSyncE2ETests
{
    private static readonly TimeSpan WatcherDeadline = TimeSpan.FromSeconds(30);

    private const string DeliveredType = "\npublic sealed class WatcherDeliveredType\n{\n}\n";

    private const string AddedRelativePath = "src/Fixture.Trading/SyncedType.cs";

    private const string AddedSource =
        "namespace Fixture.Trading;\n\npublic sealed class SyncedType\n{\n    public int Value() => 1;\n}\n";

    [Fact]
    public async Task WriteText_ThenOutlineAndReplaceSymbol_SeesTheNewFileWithoutAReload()
    {
        await using var solution = await StartAsync(watch: true);

        var written = await solution.CallAsync("write_text", new()
        {
            ["path"] = AddedRelativePath,
            ["content"] = AddedSource,
            ["force"] = true,
        });

        Assert.Contains("write_text applied", written, StringComparison.Ordinal);

        var outline = await solution.CallAsync("get_file_outline", new() { ["path"] = AddedRelativePath });

        Assert.Contains("SyncedType", outline, StringComparison.Ordinal);
        Assert.Contains("SyncedType.Value()", outline, StringComparison.Ordinal);

        var replaced = await solution.CallAsync("replace_symbol", new()
        {
            ["symbolId"] = "SyncedType.Value",
            ["declaration"] = "    public int Value() => 42;",
        });

        Assert.Contains("replace_symbol applied", replaced, StringComparison.Ordinal);
        Assert.Contains("42", replaced, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExternalCreate_ThenSearchSymbols_FindsTheNewType()
    {
        await using var solution = await StartAsync(watch: true);
        var path = Path.Combine(solution.ProjectDirectory, "SyncedType.cs");

        await File.WriteAllTextAsync(path, AddedSource, TestContext.Current.CancellationToken);
        await solution.CallAsync("get_file_outline", new() { ["path"] = AddedRelativePath });

        var found = await solution.CallAsync("search_symbols", new() { ["query"] = "SyncedType" });

        Assert.Contains("SyncedType", found, StringComparison.Ordinal);
        Assert.DoesNotContain("0 symbols", found, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExternalEdit_ThenGetSymbolSource_ReturnsTheNewBody()
    {
        await using var solution = await StartAsync(watch: true);

        await ReplaceSubmitBodyAsync(solution, "ExternallyEditedMarker");
        await solution.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderService.cs" });

        var source = await solution.CallAsync("get_symbol_source", new() { ["symbolId"] = "OrderService.Submit" });

        Assert.Contains("ExternallyEditedMarker", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExternalEdit_WithTheWatcherOff_IsStillCaughtByTheStampCheck()
    {
        await using var solution = await StartAsync(watch: false);

        var status = await solution.CallAsync("workspace_status", []);

        Assert.Contains("watch=off", status, StringComparison.Ordinal);

        await ReplaceSubmitBodyAsync(solution, "CaughtWithoutAWatcher");
        await solution.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderService.cs" });

        var source = await solution.CallAsync("get_symbol_source", new() { ["symbolId"] = "OrderService.Submit" });

        Assert.Contains("CaughtWithoutAWatcher", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExternalDelete_ThenSearchSymbols_ReportsNoPhantomHit()
    {
        await using var solution = await StartAsync(watch: false);
        var path = Path.Combine(solution.ProjectDirectory, "Awkward.cs");

        Assert.Contains("Awkward", await solution.CallAsync("search_symbols", new() { ["query"] = "Awkward" }), StringComparison.Ordinal);

        File.Delete(path);
        await solution.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/Awkward.cs" });

        var found = await solution.CallAsync("search_symbols", new() { ["query"] = "Awkward" });

        Assert.Contains("0 symbols", found, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExternalProjectFileChange_ThenAProjectCall_ReloadsAndBumpsTheProjectGeneration()
    {
        await using var solution = await StartAsync(watch: false);

        await File.WriteAllTextAsync(
            solution.ProjectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <RootNamespace>Reloaded</RootNamespace>\n  </PropertyGroup>\n</Project>\n",
            TestContext.Current.CancellationToken);

        var properties = await solution.CallAsync("project_properties", new()
        {
            ["project"] = "src/Fixture.Trading/Fixture.Trading.csproj",
        });

        Assert.Contains("Reloaded", properties, StringComparison.Ordinal);
        Assert.Contains("gen=c1/p1/x0/r0", await solution.CallAsync("workspace_status", []), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABurstOfExternalCreates_CostsOneReloadForTheWholeBurst()
    {
        await using var solution = await StartAsync(watch: true);

        for (var index = 0; index < 100; index++)
            await WriteBurstFileAsync(solution, index);

        Assert.True(
            await SettlesAsync(solution, "gen=c1/p1/x0/r0"),
            "100 external creates did not settle at one reload: " + await solution.CallAsync("workspace_status", []));
    }

    [Fact]
    public async Task ExternalEdit_WithTheWatcherOn_ReachesAToolCallThatNamesNoPath()
    {
        await using var solution = await StartAsync(watch: true);

        await MaterialiseAsync(solution);
        await AppendAsync(solution.OrderServicePath, DeliveredType);

        Assert.True(
            await FindsAsync(solution, "WatcherDeliveredType"),
            "the watcher never delivered the external edit to a hint-less search_symbols");
    }

    [Fact]
    public async Task ExternalEdit_WithTheWatcherOff_IsInvisibleUntilACallNamesThePath()
    {
        await using var solution = await StartAsync(watch: false);

        await MaterialiseAsync(solution);
        await AppendAsync(solution.OrderServicePath, DeliveredType);

        var blind = await solution.CallAsync("search_symbols", new() { ["query"] = "WatcherDeliveredType" });

        Assert.Contains("0 symbols", blind, StringComparison.Ordinal);

        await solution.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderService.cs" });

        var hinted = await solution.CallAsync("search_symbols", new() { ["query"] = "WatcherDeliveredType" });

        Assert.Contains("WatcherDeliveredType", hinted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UndoLastChange_AfterAnExternalChangeToTheEditedFile_RefusesAndSaysWhy()
    {
        await using var solution = await StartAsync(watch: false);

        var replaced = await solution.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "OrderService.Submit",
            ["body"] = "return repository.Submit(order);",
        });

        Assert.Contains("applied", replaced, StringComparison.Ordinal);

        await AppendAsync(solution.OrderServicePath, "\n// ChangedBehindTheServersBack\n");
        await solution.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderService.cs" });

        var undone = await solution.CallAsync("undo_last_change", []);

        Assert.Contains("nothing to undo", undone, StringComparison.Ordinal);
        Assert.Contains("OrderService.cs", undone, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadWorkspace_WithReload_ReportsFreshCountsAndCarriesTheGenerationsForward()
    {
        await using var solution = await StartAsync(watch: false);

        var reloaded = await solution.CallAsync("load_workspace", new()
        {
            ["path"] = solution.SolutionPath,
            ["reload"] = true,
        });

        Assert.Contains("load_workspace", reloaded, StringComparison.Ordinal);
        Assert.Contains("failures=0", reloaded, StringComparison.Ordinal);
        Assert.Contains("gen=c1/p1/x0/r0", await solution.CallAsync("workspace_status", []), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceStatus_WithTheWatcherOn_ReportsTheSyncCounters()
    {
        await using var solution = await StartAsync(watch: true);

        var status = await solution.CallAsync("workspace_status", []);

        Assert.Contains("watch=active", status, StringComparison.Ordinal);
        Assert.Contains("gen=c0/p0/x0/r0", status, StringComparison.Ordinal);
        Assert.Contains("pending=0", status, StringComparison.Ordinal);
        Assert.Contains("gaps=0", status, StringComparison.Ordinal);
    }

    private static Task<string> MaterialiseAsync(TerseTempSolution solution) =>
        solution.CallAsync("search_symbols", new() { ["query"] = "OrderService" });

    private static Task<bool> SettlesAsync(TerseTempSolution solution, string expected) =>
        PollAsync(() => solution.CallAsync("workspace_status", []), expected);

    private static Task<bool> FindsAsync(TerseTempSolution solution, string name) =>
        PollAsync(() => solution.CallAsync("search_symbols", new() { ["query"] = name }), name);

    private static async Task<bool> PollAsync(Func<Task<string>> call, string expected)
    {
        var deadline = DateTime.UtcNow + WatcherDeadline;

        while (DateTime.UtcNow < deadline)
        {
            if ((await call()).Contains(expected, StringComparison.Ordinal))
                return true;

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        return false;
    }

    private static Task<TerseTempSolution> StartAsync(bool watch) =>
        TerseTempSolution.StartAsync(watch, TestContext.Current.CancellationToken);

    private static Task WriteBurstFileAsync(TerseTempSolution solution, int index)
    {
        var name = "Burst" + index.ToString(CultureInfo.InvariantCulture);

        return File.WriteAllTextAsync(
            Path.Combine(solution.ProjectDirectory, name + ".cs"),
            string.Create(CultureInfo.InvariantCulture, $"namespace Fixture.Trading;\n\npublic sealed class {name}\n{{\n}}\n"),
            TestContext.Current.CancellationToken);
    }

    private static async Task AppendAsync(string path, string addition)
    {
        var existing = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(path, existing + addition, TestContext.Current.CancellationToken);
    }

    private static async Task ReplaceSubmitBodyAsync(TerseTempSolution solution, string marker)
    {
        var text = await File.ReadAllTextAsync(solution.OrderServicePath, TestContext.Current.CancellationToken);
        var updated = text.Replace(
            "public bool Submit(Order order) => repository.Submit(order);",
            string.Create(
                CultureInfo.InvariantCulture,
                $"public bool Submit(Order order) => repository.Submit(order); // {marker}"),
            StringComparison.Ordinal);

        Assert.NotEqual(text, updated);

        await File.WriteAllTextAsync(solution.OrderServicePath, updated, TestContext.Current.CancellationToken);
    }
}
