namespace TerseSharp.E2ETests;

public sealed class ReadOnlyServerE2ETests : IAsyncLifetime
{
    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync() => server = await TerseServerProcess.StartAsync(
        TerseServerFixture.FixtureRoot,
        [
            TerseServerFixture.ServerAssemblyPath(),
            "serve",
            "--read-only",
            "--workspace",
            Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"),
        ],
        TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await server.StopAsync();

    [Fact]
    public async Task ReadTools_StillWork()
    {
        var text = await CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderService.cs" });

        Assert.Contains("OrderService  class", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryMutatingTool_IsRefusedWithTheReadOnlyCode()
    {
        var refusals = new[]
        {
            await CallAsync("replace_symbol_body", new() { ["symbolId"] = "M:Fixture.Trading.OrderService.Unused", ["body"] = "{ return 1; }" }),
            await CallAsync("rename_symbol", new() { ["symbolId"] = "M:Fixture.Trading.OrderService.Unused", ["newName"] = "Renamed" }),
            await CallAsync("delete_symbol", new() { ["symbolId"] = "M:Fixture.Trading.OrderService.Unused" }),
            await CallAsync("write_text", new() { ["path"] = "scratch.txt", ["content"] = "x" }),
            await CallAsync("edit_text", new() { ["path"] = "appsettings.json", ["oldText"] = "100", ["newText"] = "200" }),
        };

        Assert.All(refusals, text => Assert.Contains("ERROR ReadOnly", text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefusedWrite_LeavesTheFileUntouched()
    {
        var path = Path.Combine(TerseServerFixture.FixtureRoot, "appsettings.json");
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        await CallAsync("edit_text", new() { ["path"] = "appsettings.json", ["oldText"] = "100", ["newText"] = "200" });

        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    private Task<string> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);

    public static TheoryData<string> MutatingTools() =>
    [
        "write_text", "edit_text",
        "replace_symbol_body", "replace_symbol", "add_member", "delete_symbol", "rename_symbol",
        "format", "cleanup", "clean",
        "extract_interface", "move_type_to_file", "move_type_to_namespace", "change_signature",
        "undo_last_change",
        "solution_add_project", "solution_remove_project",
        "project_create", "project_set_property", "project_add_reference", "project_remove_reference",
        "package_add", "package_remove",
        "resx_set", "resx_remove", "resx_rename",
        "razor_set_attribute", "razor_add_element", "razor_remove_element", "razor_set_directive",
    ];

    [Theory]
    [MemberData(nameof(MutatingTools))]
    public async Task EveryMutatingTool_IsRefusedBeforeItLooksAtItsArguments(string tool)
    {
        var text = await CallAsync(tool, Arguments(tool));

        Assert.Contains("ERROR ReadOnly", text, StringComparison.Ordinal);
    }

    private static Dictionary<string, object?> Arguments(string tool) => tool switch
    {
        "write_text" => new() { ["path"] = "readonly.json", ["content"] = "{}" },
        "edit_text" => new() { ["path"] = "appsettings.json", ["oldText"] = "MaxVolume", ["newText"] = "X" },
        "replace_symbol_body" => new() { ["symbolId"] = Unused, ["body"] = "{ return 1; }" },
        "replace_symbol" => new() { ["symbolId"] = Unused, ["declaration"] = "public int Unused() => 1;" },
        "add_member" => new() { ["typeSymbolId"] = ServiceType, ["declaration"] = "public int A() => 1;" },
        "delete_symbol" => new() { ["symbolId"] = Unused },
        "rename_symbol" => new() { ["symbolId"] = Unused, ["newName"] = "Spare" },
        "format" or "cleanup" => new() { ["path"] = "src/Fixture.Trading/Order.cs" },
        "extract_interface" => new() { ["typeSymbolId"] = ServiceType, ["interfaceName"] = "IOrderService" },
        "move_type_to_file" => new() { ["typeSymbolId"] = ServiceType },
        "move_type_to_namespace" => new() { ["typeSymbolId"] = ServiceType, ["targetNamespace"] = "Other" },
        "change_signature" => new() { ["symbolId"] = Unused, ["parameters"] = "int factor" },
        "undo_last_change" => [],
        "solution_add_project" or "solution_remove_project" => new() { ["project"] = ProjectFile },
        "project_create" => new() { ["project"] = "src/New/New.csproj" },
        "project_set_property" => new() { ["project"] = ProjectFile, ["name"] = "LangVersion", ["value"] = "preview" },
        "project_add_reference" or "project_remove_reference" => new() { ["project"] = ProjectFile, ["target"] = ProjectFile },
        "package_add" => new() { ["project"] = ProjectFile, ["package"] = "Serilog", ["version"] = "4.0.0" },
        "resx_set" => new() { ["path"] = ResourceFile, ["key"] = "Scratch_Two", ["value"] = "Two" },
        "resx_remove" => new() { ["path"] = ResourceFile, ["key"] = "Scratch_One", ["force"] = true },
        "resx_rename" => new() { ["path"] = ResourceFile, ["key"] = "Scratch_One", ["newKey"] = "Scratch_Renamed" },
        "razor_set_attribute" => new() { ["path"] = RazorFile, ["target"] = "div", ["attribute"] = "class", ["value"] = "x" },
        "razor_add_element" => new() { ["path"] = RazorFile, ["parent"] = "div", ["markup"] = "<span />" },
        "razor_remove_element" => new() { ["path"] = RazorFile, ["target"] = "div" },
        "razor_set_directive" => new() { ["path"] = RazorFile, ["directive"] = "using", ["value"] = "System" },
        _ => new() { ["project"] = ProjectFile, ["package"] = "Serilog" },
    };

    private const string ProjectFile = "src/Fixture.Trading/Fixture.Trading.csproj";

    private const string RazorFile = "src/Fixture.Trading/Views/Home.razor";

    private const string ServiceType = "T:Fixture.Trading.OrderService";

    private const string Unused = "M:Fixture.Trading.OrderService.Unused";

    private const string ResourceFile = "src/Fixture.Trading/Scratch.resx";
}
