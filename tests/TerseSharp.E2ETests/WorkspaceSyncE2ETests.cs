
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

        Assert.Contains("changedLines=", written, StringComparison.Ordinal);

        var outline = await solution.CallAsync("get_file_outline", new() { ["path"] = AddedRelativePath });

        Assert.Contains("SyncedType", outline, StringComparison.Ordinal);
        Assert.Contains("  SyncedType.Value  ", outline, StringComparison.Ordinal);

        var replaced = await solution.CallAsync("replace_symbol", new()
        {
            ["symbolId"] = "SyncedType.Value",
            ["declaration"] = "    public int Value() => 42;",
            ["verbose"] = true,
        });

        Assert.Contains("changedLines=", replaced, StringComparison.Ordinal);
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
        Assert.Contains("gen=c1/p1/x0/r0", await Status(solution), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABurstOfExternalCreates_CostsOneReloadForTheWholeBurst()
    {
        await using var solution = await StartAsync(watch: true);

        for (var index = 0; index < 100; index++)
            await WriteBurstFileAsync(solution, index);

        Assert.True(
            await SettlesAsync(solution, "gen=c1/p1/x0/r0"),
            "100 external creates did not settle at one reload: " + await Status(solution));
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

        Assert.Contains("changedLines=", replaced, StringComparison.Ordinal);

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

        Assert.Contains("failures=0", reloaded, StringComparison.Ordinal);
        Assert.Contains("gen=c1/p1/x0/r0", await Status(solution), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceStatus_WithTheWatcherOn_ReportsTheSyncCounters()
    {
        await using var solution = await StartAsync(watch: true);

        var status = await Status(solution);

        Assert.Contains("watch=active", status, StringComparison.Ordinal);
        Assert.Contains("gen=c0/p0/x0/r0", status, StringComparison.Ordinal);
        Assert.Contains("pending=0", status, StringComparison.Ordinal);
        Assert.Contains("gaps=0", status, StringComparison.Ordinal);
    }

    private static Task<string> MaterialiseAsync(TerseTempSolution solution) =>
        solution.CallAsync("search_symbols", new() { ["query"] = "OrderService" });

    private static Task<bool> SettlesAsync(TerseTempSolution solution, string expected) =>
        PollAsync(() => Status(solution), expected);

    private static Task<string> Status(TerseTempSolution solution) =>
        solution.CallAsync("workspace_status", new() { ["verbose"] = true });

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

    [Fact]
    public async Task EditText_ThenASymbolEditOnTheSameFile_KeepsBothChanges()
    {
        await using var solution = await StartAsync(watch: true);
        await solution.CallAsync("write_text", new()
        {
            ["path"] = AddedRelativePath,
            ["content"] = AddedSource,
            ["force"] = true,
        });
        var edited = await solution.CallAsync("edit_text", new()
        {
            ["path"] = AddedRelativePath,
            ["oldText"] = "public sealed class SyncedType",
            ["newText"] = "public sealed class SyncedType // marker",
            ["force"] = true,
        });
        Assert.Contains("changedLines=", edited, StringComparison.Ordinal);
        var replaced = await solution.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "SyncedType.Value",
            ["body"] = "=> 42;",
        });
        Assert.DoesNotContain("ERROR", replaced, StringComparison.Ordinal);
        var after = await solution.CallAsync("read_text", new() { ["path"] = AddedRelativePath, ["verbose"] = true });
        Assert.Contains("// marker", after, StringComparison.Ordinal);
        Assert.Contains("Value() => 42;", after, StringComparison.Ordinal);
    }
    [Fact]
    public async Task ExternalCreate_OfANonCodeFile_IsListedByFindFilesWithoutAReload()
    {
        await using var solution = await StartAsync(watch: true);

        await solution.CallAsync("find_files", new() { ["glob"] = "**/*.md" });

        await File.WriteAllTextAsync(
            Path.Combine(solution.ProjectDirectory, "SyncedNote.md"),
            "# synced\n",
            TestContext.Current.CancellationToken);

        Assert.True(await PollAsync(
            () => solution.CallAsync("find_files", new() { ["glob"] = "**/*.md" }),
            "SyncedNote.md"));
    }

    [Fact]
    public async Task SearchText_InAUtf16EncodedFile_StillFindsTheToken()
    {
        await using var solution = await StartAsync(watch: true);

        await File.WriteAllTextAsync(
            Path.Combine(solution.ProjectDirectory, "Utf16Note.txt"),
            "WideEncodedMarker\n",
            new System.Text.UnicodeEncoding(false, true),
            TestContext.Current.CancellationToken);

        Assert.True(await PollAsync(
            () => solution.CallAsync("search_text", new() { ["query"] = "WideEncodedMarker" }),
            "Utf16Note.txt"));
    }

    [Fact]
    public async Task ExternalDelete_OfANonCodeFile_DropsItFromFindFiles()
    {
        await using var solution = await StartAsync(watch: true);
        var path = Path.Combine(solution.ProjectDirectory, "DoomedNote.md");

        await File.WriteAllTextAsync(path, "# doomed\n", TestContext.Current.CancellationToken);

        Assert.True(await PollAsync(
            () => solution.CallAsync("find_files", new() { ["glob"] = "**/Doomed*.md" }),
            "DoomedNote.md"));

        File.Delete(path);

        Assert.True(await PollAsync(
            () => solution.CallAsync("find_files", new() { ["glob"] = "**/Doomed*.md" }),
            "0 files"));
    }

    private const string SecondOfTwo = "\npublic sealed class SecondOfTwoMarkerType;\n";

    [Fact]
    public async Task ASecondServerOverTheSameRoot_ConvergesOnTwoWritesInQuickSuccession()
    {
        await using var solution = await StartAsync(watch: true);
        var second = await solution.AttachAsync(watch: true, TestContext.Current.CancellationToken);
        await second.CallAsync(
            "get_symbol_source",
            new() { ["symbolId"] = "OrderService.Submit" },
            TestContext.Current.CancellationToken);
        await ReplaceSubmitBodyAsync(solution, "FirstOfTwoMarker");
        await AppendAsync(solution.OrderServicePath, SecondOfTwo);
        Assert.True(
            await PollAsync(
                () => second.CallAsync(
                    "search_symbols",
                    new() { ["query"] = "SecondOfTwoMarkerType" },
                    TestContext.Current.CancellationToken),
                "SecondOfTwoMarkerType"),
            "the second server never converged on the newer of two writes made in quick succession");
    }

    [Fact]
    public async Task WriteText_OverAKnownFileReferencingAFileJustCreated_IsNotRolledBack()
    {
        await using var solution = await StartAsync(watch: false);

        const string HostPath = "src/Fixture.Trading/GateHost.cs";
        const string CalleePath = "src/Fixture.Trading/GateCallee.cs";

        var host = await solution.CallAsync("write_text", new()
        {
            ["path"] = HostPath,
            ["content"] = "namespace Fixture.Trading;\n\npublic static class GateHost\n{\n    public static int Seed() => 1;\n}\n",
            ["force"] = true,
        });
        var outline = await solution.CallAsync("get_file_outline", new() { ["path"] = HostPath });
        var callee = await solution.CallAsync("write_text", new()
        {
            ["path"] = CalleePath,
            ["content"] = "namespace Fixture.Trading;\n\npublic static class GateCallee\n{\n    public static int Extra() => 41;\n}\n",
            ["force"] = true,
        });
        var rewritten = await solution.CallAsync("write_text", new()
        {
            ["path"] = HostPath,
            ["content"] = "namespace Fixture.Trading;\n\npublic static class GateHost\n{\n    public static int Seed() => GateCallee.Extra() + 1;\n}\n",
            ["force"] = true,
        });

        Assert.Contains("changedLines=", host, StringComparison.Ordinal);
        Assert.Contains("GateHost.Seed", outline, StringComparison.Ordinal);
        Assert.Contains("changedLines=", callee, StringComparison.Ordinal);
        Assert.DoesNotContain("rolled back", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0246", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0103", rewritten, StringComparison.Ordinal);
        Assert.Contains("changedLines=", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadWorkspace_ReportsColdCompilations_AndTheFirstSemanticCallReportsRealizingThem()
    {
        await using var solution = await StartAsync(watch: false);

        var loaded = await solution.CallAsync("load_workspace", new() { ["reload"] = true });
        var first = await solution.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderService.cs" });
        var second = await solution.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderBook.cs" });

        Assert.Contains("compilations=cold", loaded, StringComparison.Ordinal);
        Assert.Contains("compilations=realized in ", first, StringComparison.Ordinal);
        Assert.DoesNotContain("compilations=", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceStatus_WhenDiskNoLongerMatchesTheWorkspace_NamesTheDivergedDocument()
    {
        await using var solution = await StartAsync(watch: false);

        var applied = await solution.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Unused",
            ["body"] = "=> 41;",
        });

        Assert.Contains("changedLines=", applied, StringComparison.Ordinal);

        var clean = await solution.CallAsync("workspace_status", new() { ["verbose"] = true });

        Assert.Contains("disk=in-sync", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("workspace=diverged", clean, StringComparison.Ordinal);

        var onDisk = await File.ReadAllTextAsync(solution.OrderServicePath, TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(
            solution.OrderServicePath,
            onDisk.Replace("=> 41;", "=> 7;", StringComparison.Ordinal),
            TestContext.Current.CancellationToken);

        var diverged = await solution.CallAsync("workspace_status", []);

        Assert.Contains("workspace=diverged", diverged, StringComparison.Ordinal);
        Assert.Contains("OrderService.cs", diverged, StringComparison.Ordinal);
        Assert.Contains("load_workspace reload=true", diverged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentNewCSharpWrites_IntoOneProject_AnswerOnlyASuccessOrAnEditConflict()
    {
        await using var solution = await StartAsync(watch: true);

        var written = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => WriteConcurrentAsync(solution, index)));

        Assert.All(written, text => Assert.DoesNotContain("ERROR Internal", text, StringComparison.Ordinal));
        Assert.All(written, text => Assert.DoesNotContain("ERROR Transient", text, StringComparison.Ordinal));
        Assert.All(written, text => Assert.DoesNotContain("UnauthorizedAccessException", text, StringComparison.Ordinal));
        Assert.All(written, text => Assert.True(
            text.Contains("changedLines=", StringComparison.Ordinal) || text.Contains("ERROR EditConflict", StringComparison.Ordinal),
            text));
        Assert.Contains(written, text => text.Contains("changedLines=", StringComparison.Ordinal));

        for (var index = 0; index < 6; index++)
        {
            var retried = await WriteConcurrentAsync(solution, index);

            Assert.DoesNotContain("ERROR", retried, StringComparison.Ordinal);
        }

        var outline = await solution.CallAsync("get_file_outline", new()
        {
            ["paths"] = Enumerable.Range(0, 6)
                .Select(index => string.Create(CultureInfo.InvariantCulture, $"src/Fixture.Trading/Concurrent{index}.cs"))
                .ToArray(),
        });

        Assert.All(
            Enumerable.Range(0, 6),
            index => Assert.Contains(
                string.Create(CultureInfo.InvariantCulture, $"Concurrent{index}.Value"),
                outline,
                StringComparison.Ordinal));
    }

    private static Task<string> WriteConcurrentAsync(TerseTempSolution solution, int index) =>
        solution.CallAsync("write_text", new()
        {
            ["path"] = string.Create(CultureInfo.InvariantCulture, $"src/Fixture.Trading/Concurrent{index}.cs"),
            ["content"] = string.Create(
                CultureInfo.InvariantCulture,
                $"namespace Fixture.Trading;\n\npublic sealed class Concurrent{index}\n{{\n    public int Value() => {index};\n}}\n"),
            ["force"] = true,
        });
}
