namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class EditToolsE2ETests(TerseServerFixture server)
{
    private const string UnusedMethod = "M:Fixture.Trading.OrderService.Unused";
    private static readonly string OrderServicePath =
        Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "OrderService.cs");

    [Fact]
    public async Task ReplaceSymbolBody_WithDryRun_ReturnsDiffAndLeavesTheFileUntouched()
    {
        var before = await File.ReadAllTextAsync(OrderServicePath, TestContext.Current.CancellationToken);

        var text = await server.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = UnusedMethod,
            ["body"] = "{ return 9; }",
            ["dryRun"] = true,
        });

        Assert.Contains("dryRun", text, StringComparison.Ordinal);
        Assert.Contains("+", text, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(OrderServicePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSymbolBody_ThatWouldNotCompile_IsRolledBack()
    {
        var before = await File.ReadAllTextAsync(OrderServicePath, TestContext.Current.CancellationToken);

        var text = await server.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = UnusedMethod,
            ["body"] = "{ return \"not an int\"; }",
            ["dryRun"] = false,
        });

        Assert.Contains("ERROR CompileRegression", text, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(OrderServicePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSymbol_WithLiveReferences_IsRefusedAndListsThem()
    {
        var text = await server.CallAsync("delete_symbol", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)",
            ["force"] = false,
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("usages", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameSymbol_WithDryRun_TouchesEveryReferencingFile()
    {
        var text = await server.CallAsync("rename_symbol", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)",
            ["newName"] = "SubmitAsync",
            ["dryRun"] = true,
        });

        Assert.Contains("OrderService.cs", text, StringComparison.Ordinal);
        Assert.Contains("OrderRouter.cs", text, StringComparison.Ordinal);
        Assert.Contains("2 files changed", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameSymbol_WithAnInvalidIdentifier_IsRefused()
    {
        var text = await server.CallAsync("rename_symbol", new()
        {
            ["symbolId"] = UnusedMethod,
            ["newName"] = "9not valid",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithDryRun_ShowsTheInsertedMember()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderService",
            ["declaration"] = "public int Added() => 1;",
            ["dryRun"] = true,
        });

        Assert.Contains("+", text, StringComparison.Ordinal);
        Assert.Contains("Added", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_WithAnAmbiguousMatch_IsRefused()
    {
        var text = await server.CallAsync("edit_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["oldText"] = "order",
            ["newText"] = "request",
            ["dryRun"] = true,
            ["force"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("expected exactly 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_OnACsFileWithoutForce_PointsAtTheSemanticTools()
    {
        var text = await server.CallAsync("edit_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["oldText"] = "Unused",
            ["newText"] = "Spare",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("replace_symbol_body", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_Applied_ChangesTheFileOnDiskAndIsReadableBack()
    {
        var path = Path.Combine(TerseServerFixture.FixtureRoot, "scratch.json");

        try
        {
            var written = await server.CallAsync("write_text", new()
            {
                ["path"] = "scratch.json",
                ["content"] = "{ \"written\": true }",
                ["dryRun"] = false,
            });

            Assert.Contains("applied", written, StringComparison.Ordinal);
            Assert.Equal("{ \"written\": true }", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));

            var read = await server.CallAsync("read_text", new() { ["path"] = "scratch.json" });

            Assert.Contains("\"written\": true", read, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WriteText_OutsideTheWorkspace_IsRefused()
    {
        var text = await server.CallAsync("write_text", new()
        {
            ["path"] = "../../escaped.txt",
            ["content"] = "no",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR OutOfWorkspace", text, StringComparison.Ordinal);
    }
}
