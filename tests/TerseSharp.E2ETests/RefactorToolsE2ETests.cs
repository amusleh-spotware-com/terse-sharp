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

    [Fact]
    public async Task ExtractInterface_WhenApplied_WritesTheNewDocumentToDisk()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: false, TestContext.Current.CancellationToken);

        var text = await solution.CallAsync("extract_interface", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderService",
            ["interfaceName"] = "IExtractedOrders",
        });

        Assert.Contains("changedLines=", text, StringComparison.Ordinal);

        var created = Path.Combine(solution.ProjectDirectory, "IExtractedOrders.cs");

        Assert.True(File.Exists(created), created);

        var content = await File.ReadAllTextAsync(created, TestContext.Current.CancellationToken);

        Assert.Contains("interface IExtractedOrders", content, StringComparison.Ordinal);
        Assert.Contains("Submit", content, StringComparison.Ordinal);

        Assert.Contains(
            "IExtractedOrders",
            await solution.CallAsync("get_symbol", new() { ["symbolId"] = "T:Fixture.Trading.IExtractedOrders" }),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UndoLastChange_AfterEditingAFileTheSessionCreated_ActuallyRevertsIt()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: false, TestContext.Current.CancellationToken);

        await solution.CallAsync("extract_interface", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderService",
            ["interfaceName"] = "IUndoProbe",
        });

        var created = Path.Combine(solution.ProjectDirectory, "IUndoProbe.cs");
        var extracted = await File.ReadAllTextAsync(created, TestContext.Current.CancellationToken);

        var added = await solution.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.IUndoProbe",
            ["declaration"] = "int Probe();",
        });

        Assert.Contains("changedLines=", added, StringComparison.Ordinal);
        Assert.Contains("int Probe();", await File.ReadAllTextAsync(created, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        Assert.Contains("reverted", await solution.CallAsync("undo_last_change", []), StringComparison.Ordinal);
        Assert.Equal(extracted, await File.ReadAllTextAsync(created, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MoveTypeToFile_WhenApplied_WritesTheTypeOutAndRemovesItFromTheOrigin()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: false, TestContext.Current.CancellationToken);

        var origin = Path.Combine(solution.ProjectDirectory, "Awkward.cs");
        var text = await solution.CallAsync("move_type_to_file", new() { ["typeSymbolId"] = "T:Fixture.Trading.IHandler" });

        Assert.Contains("changedLines=", text, StringComparison.Ordinal);

        var created = Path.Combine(solution.ProjectDirectory, "IHandler.cs");

        Assert.True(File.Exists(created), created);
        Assert.Contains(
            "interface IHandler",
            await File.ReadAllTextAsync(created, TestContext.Current.CancellationToken),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "interface IHandler",
            await File.ReadAllTextAsync(origin, TestContext.Current.CancellationToken),
            StringComparison.Ordinal);

        Assert.Contains(
            "IHandler",
            await solution.CallAsync("get_symbol", new() { ["symbolId"] = "T:Fixture.Trading.IHandler" }),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoveTypeToFile_WhenTheSiblingLivesInASubdirectory_WritesTheNewFileBesideIt()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: false, TestContext.Current.CancellationToken);

        var text = await solution.CallAsync("move_type_to_file", new() { ["typeSymbolId"] = "T:Fixture.Trading.Views.ShellView" });

        var beside = Path.Combine(solution.ProjectDirectory, "Views", "ShellView.cs");
        var flattened = Path.Combine(solution.ProjectDirectory, "ShellView.cs");

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("Views", "ShellView.cs"), text, StringComparison.Ordinal);
        Assert.False(File.Exists(flattened), "move_type_to_file flattened the new file onto the project root: " + flattened);
        Assert.True(File.Exists(beside), "move_type_to_file reported a nested path it never wrote: " + beside);
    }
}
