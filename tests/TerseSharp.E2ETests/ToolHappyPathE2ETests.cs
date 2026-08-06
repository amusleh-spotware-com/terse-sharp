using System.Security.Cryptography;

namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class ToolHappyPathE2ETests(TerseServerFixture server)
{
    private const string Project = "src/Fixture.Trading/Fixture.Trading.csproj";

    private const string ServiceType = "T:Fixture.Trading.OrderService";

    private const string UnusedMethod = "M:Fixture.Trading.OrderService.Unused";

    private const string SubmitMethod = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)";

    private const string View = "src/Fixture.Trading/Views/OrderView.xaml";

    private static readonly string[] SpawnAProcess = ["build", "run_tests", "rerun_failed", "list_tests"];

    private static readonly string[] ErrorPathOnly = ["unload_workspace", "undo_last_change", "package_add", "package_remove"];

    private static readonly string[] NeedRazorFixture =
    [
        "razor_outline", "razor_component", "razor_find", "razor_bindings", "razor_codebehind",
        "razor_validate", "razor_set_attribute", "razor_add_element", "razor_remove_element", "razor_set_directive",
    ];

    public static TheoryData<string> HappyPath() => [.. Cases.Select(entry => entry.Tool)];

    [Theory]
    [MemberData(nameof(HappyPath))]
    public async Task EveryTool_WithValidArguments_ProducesItsExpectedRecord(string tool)
    {
        var (_, arguments, expect) = Cases.Single(entry => entry.Tool == tool);
        var text = await server.CallAsync(tool, arguments);

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.DoesNotContain(tool + " ", text.Split('\n')[0], StringComparison.Ordinal);
        Assert.Contains(expect, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryAdvertisedTool_IsEitherOnTheHappyPathOrExplicitlyAccountedFor()
    {
        var accounted = Accounted();

        Assert.DoesNotContain(await Advertised(), name => !accounted.Contains(name));
    }

    [Fact]
    public async Task NothingIsAccountedForThatTheServerNoLongerAdvertises()
    {
        var advertised = (await Advertised()).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(Accounted(), name => !advertised.Contains(name));
    }

    [Fact]
    public async Task TheWholeHappyPath_LeavesEveryFixtureFileByteForByteUnchanged()
    {
        var before = Snapshot();

        foreach (var (tool, arguments, _) in Cases)
            await server.CallAsync(tool, arguments);

        var after = Snapshot();

        Assert.Equal(before.Count, after.Count);
        Assert.All(after, entry => Assert.True(
            before.TryGetValue(entry.Key, out var hash) && hash == entry.Value,
            entry.Key + " changed while the happy path ran"));
    }

    private async Task<string[]> Advertised() =>
        [.. (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).Select(tool => tool.Name)];

    private static HashSet<string> Accounted() =>
        [.. Cases.Select(entry => entry.Tool).Concat(SpawnAProcess).Concat(ErrorPathOnly).Concat(NeedRazorFixture)];

    private static Dictionary<string, string> Snapshot()
    {
        var files = Directory
            .EnumerateFiles(TerseServerFixture.FixtureRoot, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        return files.ToDictionary(
            path => Path.GetRelativePath(TerseServerFixture.FixtureRoot, path),
            path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            StringComparer.OrdinalIgnoreCase);
    }

    private static (string Tool, Dictionary<string, object?> Arguments, string Expect)[] Cases =>
    [
        ("load_workspace", new() { ["path"] = Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx") }, "projects=1"),
        ("workspace_status", [], "documents="),
        ("list_workspaces", [], "worktree="),
        ("list_projects", [], "Fixture.Trading"),
        ("search_symbols", new() { ["query"] = "OrderService" }, "T:Fixture.Trading.OrderService"),
        ("get_symbol", new() { ["symbolId"] = ServiceType }, "class public"),
        ("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderService.cs" }, "  OrderService.Submit  "),
        ("get_type_outline", new() { ["symbolId"] = ServiceType }, "PendingCount"),
        ("get_symbol_source", new() { ["symbolId"] = SubmitMethod }, "repository.Submit(order)"),
        ("find_usages", new() { ["symbolId"] = SubmitMethod }, "OrderRouter.cs"),
        ("find_implementations", new() { ["symbolId"] = "M:Fixture.Trading.IOrderRepository.Submit(Fixture.Trading.Order)" }, "InMemoryOrderRepository"),
        ("get_diagnostics", new() { ["minSeverity"] = "error" }, "0 diagnostics"),
        ("analyze", new() { ["minSeverity"] = "warning" }, "engines=compiler"),
        ("format", new() { ["path"] = "src/Fixture.Trading/Order.cs", ["dryRun"] = true }, "dryRun"),
        ("cleanup", new() { ["path"] = "src/Fixture.Trading/OrderService.cs", ["dryRun"] = true }, "files changed"),
        ("clean", new() { ["dryRun"] = true }, "directories"),
        ("read_text", new() { ["path"] = "appsettings.json" }, "1: "),
        ("write_text", new() { ["path"] = "happy.json", ["content"] = "{}", ["dryRun"] = true }, "changedLines="),
        ("edit_text", new() { ["path"] = "appsettings.json", ["oldText"] = "MaxVolume", ["newText"] = "PeakVolume", ["dryRun"] = true }, "changedLines="),
        ("find_files", new() { ["glob"] = "*.cs" }, "OrderService.cs"),
        ("changed_files", new() { ["maxResults"] = 5 }, "files"),
        ("diff_symbols", new() { ["maxResults"] = 5 }, "declarations"),
        ("diff_text", new() { ["maxLines"] = 5 }, "lines"),
        ("search_text", new() { ["pattern"] = "Order", ["glob"] = "*.cs" }, "HEURISTIC"),
        ("search_regex", new() { ["pattern"] = "public", ["glob"] = "*.cs" }, "HEURISTIC"),
        ("replace_symbol_body", new() { ["symbolId"] = UnusedMethod, ["body"] = "{ return 9; }", ["dryRun"] = true }, "changedLines="),
        ("replace_symbol", new() { ["symbolId"] = UnusedMethod, ["declaration"] = "public int Unused() => 8;", ["dryRun"] = true }, "changedLines="),
        ("add_member", new() { ["typeSymbolId"] = ServiceType, ["declaration"] = "public int Added() => 1;", ["dryRun"] = true }, "changedLines="),
        ("delete_symbol", new() { ["symbolId"] = UnusedMethod, ["dryRun"] = true }, "changedLines="),
        ("rename_symbol", new() { ["symbolId"] = UnusedMethod, ["newName"] = "Spare", ["dryRun"] = true }, "changedLines="),
        ("extract_interface", new() { ["typeSymbolId"] = ServiceType, ["interfaceName"] = "IOrderService", ["dryRun"] = true }, "IOrderService"),
        ("move_type_to_file", new() { ["typeSymbolId"] = "T:Fixture.Trading.OrderSubmitted", ["dryRun"] = true }, "changedLines="),
        ("move_type_to_namespace", new() { ["typeSymbolId"] = "T:Fixture.Trading.OrderRouter", ["targetNamespace"] = "Fixture.Routing", ["dryRun"] = true }, "Fixture.Routing"),
        ("change_signature", new() { ["symbolId"] = UnusedMethod, ["parameters"] = "int factor", ["dryRun"] = true }, "changedLines="),
        ("solution_projects", [], "Fixture.Trading"),
        ("solution_add_project", new() { ["project"] = "src/Fixture.Extra/Fixture.Extra.csproj", ["dryRun"] = true }, "Fixture.Extra"),
        ("solution_remove_project", new() { ["project"] = Project, ["dryRun"] = true }, "Fixture.Trading"),
        ("project_create", new() { ["project"] = "src/Fixture.New/Fixture.New.csproj", ["kind"] = "console", ["dryRun"] = true }, "Fixture.New"),
        ("project_properties", new() { ["project"] = Project }, "properties"),
        ("project_set_property", new() { ["project"] = Project, ["name"] = "LangVersion", ["value"] = "preview", ["dryRun"] = true }, "LangVersion"),
        ("project_add_reference", new() { ["project"] = Project, ["target"] = "src/Fixture.Other/Fixture.Other.csproj", ["dryRun"] = true }, "Fixture.Other"),
        ("project_remove_reference", new()
        {
            ["project"] = "tests/Fixture.Trading.Tests/Fixture.Trading.Tests.csproj",
            ["target"] = Project,
            ["dryRun"] = true,
        }, "Fixture.Trading"),
        ("package_list", new() { ["project"] = Project }, "references"),
        ("xaml_outline", new() { ["path"] = View }, "Button"),
        ("xaml_names", new() { ["path"] = View }, "VolumeText"),
        ("xaml_resources", new() { ["path"] = View }, "resources"),
        ("xaml_bindings", new() { ["path"] = View }, "bindings"),
        ("xaml_validate", new() { ["path"] = View }, "dialect=wpf"),
        ("xaml_find", new() { ["query"] = "Button" }, "OrderView.xaml"),
        ("xaml_resolve", new() { ["key"] = "AccentBrush" }, "scope="),
        ("explore_symbol", new() { ["symbolId"] = SubmitMethod }, "usages="),
        ("impact_of", new() { ["symbolId"] = SubmitMethod }, "projects that would recompile"),
        ("find_registrations", new() { ["query"] = "IOrderRepository" }, "AddSingleton"),
        ("list_endpoints", [], "MapGet"),
        ("xaml_styles", new() { ["typeName"] = "Button" }, "targets=Button"),
        ("xaml_localization", [], "uid="),
        ("xaml_add_element", new()
        {
            ["path"] = "src/Fixture.Trading/Views/OrderView.xaml",
            ["target"] = "Window/Grid",
            ["markup"] = "<TextBlock Text=\"Added\" />",
            ["dryRun"] = true,
        }, "changedLines="),
        ("xaml_remove_element", new()
        {
            ["path"] = "src/Fixture.Trading/Views/OrderView.xaml",
            ["target"] = "#VolumeText",
            ["dryRun"] = true,
        }, "changedLines="),
        ("xaml_codebehind", new() { ["path"] = "src/Fixture.Trading/Views/ShellView.xaml" }, "class="),
        ("resx_files", [], "families"),
        ("resx_get", new() { ["path"] = "src/Fixture.Trading/Strings.resx" }, "Caption_Submit"),
        ("resx_find", new() { ["query"] = "Caption_Count" }, "Caption_Count"),
        ("resx_usages", new() { ["key"] = "Caption_Submit" }, "composedLookups="),
        ("resx_set", new()
        {
            ["path"] = "src/Fixture.Trading/Scratch.resx",
            ["key"] = "Scratch_Two",
            ["value"] = "Two",
            ["dryRun"] = true,
        }, "changedLines="),
        ("resx_remove", new()
        {
            ["path"] = "src/Fixture.Trading/Scratch.resx",
            ["key"] = "Scratch_One",
            ["force"] = true,
            ["dryRun"] = true,
        }, "changedLines="),
        ("resx_rename", new()
        {
            ["path"] = "src/Fixture.Trading/Scratch.resx",
            ["key"] = "Scratch_One",
            ["newKey"] = "Scratch_Renamed",
            ["dryRun"] = true,
        }, "changedLines="),
        ("resx_validate", new() { ["rules"] = "RESX001" }, "RESX001"),
        ("xaml_set_property", new()
        {
            ["path"] = "src/Fixture.Trading/Views/OrderView.xaml",
            ["target"] = "#SymbolText",
            ["property"] = "FontSize",
            ["value"] = "14",
            ["dryRun"] = true,
        }, "changedLines="),
    ];
}
