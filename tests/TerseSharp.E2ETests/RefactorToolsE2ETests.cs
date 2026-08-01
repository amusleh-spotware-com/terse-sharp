namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class RefactorToolsE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task ExtractInterface_WithDryRun_DeclaresThePublicMembers()
    {
        var text = await server.CallAsync("extract_interface", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderService",
            ["interfaceName"] = "IOrderService",
            ["dryRun"] = true,
        });

        Assert.Contains("interface IOrderService", text, StringComparison.Ordinal);
        Assert.Contains("Submit", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractInterface_OnAMethodId_IsRefused()
    {
        var text = await server.CallAsync("extract_interface", new()
        {
            ["typeSymbolId"] = "M:Fixture.Trading.OrderService.Unused",
            ["interfaceName"] = "INope",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoveTypeToFile_WhenTheTypeAlreadyOwnsItsFile_IsRefused()
    {
        var text = await server.CallAsync("move_type_to_file", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderService",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("already lives in its own file", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoveTypeToNamespace_WithDryRun_RewritesTheNamespace()
    {
        var text = await server.CallAsync("move_type_to_namespace", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderRouter",
            ["targetNamespace"] = "Fixture.Routing",
            ["dryRun"] = true,
        });

        Assert.Contains("Fixture.Routing", text, StringComparison.Ordinal);
        Assert.Contains("dryRun", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangeSignature_WithDryRun_RewritesTheParameterList()
    {
        var text = await server.CallAsync("change_signature", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Unused",
            ["parameters"] = "int factor",
            ["dryRun"] = true,
        });

        Assert.Contains("int factor", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangeSignature_WithAnUnparseableList_IsRefused()
    {
        var text = await server.CallAsync("change_signature", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Unused",
            ["parameters"] = "int (((",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UndoLastChange_OnAFreshWorkspaceWithNothingApplied_SaysSo()
    {
        var fresh = await TerseServerProcess.StartAsync(
            TerseServerFixture.FixtureRoot,
            [
                TerseServerFixture.ServerAssemblyPath(),
                "serve",
                "--workspace",
                Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"),
            ],
            TestContext.Current.CancellationToken);

        try
        {
            var text = await fresh.CallAsync("undo_last_change", [], TestContext.Current.CancellationToken);

            Assert.Contains("nothing to undo", text, StringComparison.Ordinal);
        }
        finally
        {
            await fresh.StopAsync();
        }
    }
}
