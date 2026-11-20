namespace TerseSharp.E2ETests;

public sealed class EditGateTimingE2ETests : IAsyncLifetime
{
    private static readonly string FixtureRoot = Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "FixtureSolution");

    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync() =>
        server = await TerseServerProcess.StartAsync(
            FixtureRoot,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(FixtureRoot, "FixtureSolution.slnx")],
            new Dictionary<string, string> { ["TERSE_GATE_TIMING_MS"] = "0" },
            TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => server.StopAsync();

    [Fact]
    public async Task EditText_OnACsFileOverTheSlowThreshold_EndsWithOneTimingLineNamingEveryPhaseAndTheProjectScope()
    {
        var edit = await server.CallAsync(
            "edit_text",
            new Dictionary<string, object?> { ["path"] = "src/Fixture.Trading/OrderBook.cs", ["oldText"] = "return removed;", ["newText"] = "return  removed;", ["dryRun"] = true },
            TestContext.Current.CancellationToken);

        Assert.Equal(1, edit.Split("\ntiming totalMs=").Length - 1);
        Assert.Contains(" diffMs=", edit, StringComparison.Ordinal);
        Assert.Contains(" shapeMs=", edit, StringComparison.Ordinal);
        Assert.Contains(" realizeMs=", edit, StringComparison.Ordinal);
        Assert.Contains(" compileMs=", edit, StringComparison.Ordinal);
        Assert.Contains(" hintsMs=", edit, StringComparison.Ordinal);
        Assert.Contains(" policyMs=", edit, StringComparison.Ordinal);
        Assert.Contains(" applyMs=", edit, StringComparison.Ordinal);
        Assert.Contains(" gate=project projects=1", edit, StringComparison.Ordinal);
    }
}
