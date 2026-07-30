namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class AnalysisToolsE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task Analyze_NamesTheEnginesItRan()
    {
        var text = await server.CallAsync("analyze", new() { ["minSeverity"] = "warning" });

        Assert.Contains("engines=compiler", text, StringComparison.Ordinal);
        Assert.Contains("diagnostics", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_ReachesBelowWarning_WhereGetDiagnosticsStops()
    {
        var info = await server.CallAsync("analyze", new() { ["minSeverity"] = "hidden" });
        var errors = await server.CallAsync("analyze", new() { ["minSeverity"] = "error" });

        Assert.True(Total(info) >= Total(errors), $"hidden={Total(info)} error={Total(errors)}");
    }

    [Fact]
    public async Task Analyze_WithAnIdFilter_ReturnsOnlyThatId()
    {
        var text = await server.CallAsync("analyze", new() { ["ids"] = "CS9999", ["minSeverity"] = "hidden" });

        Assert.Contains("0 diagnostics", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Format_WithDryRun_LeavesTheFileUntouched()
    {
        var path = Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "Order.cs");
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        var text = await server.CallAsync("format", new()
        {
            ["path"] = "src/Fixture.Trading/Order.cs",
            ["dryRun"] = true,
        });

        Assert.Contains("dryRun", text, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cleanup_WithDryRun_ReportsWhatItWouldChange()
    {
        var text = await server.CallAsync("cleanup", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["dryRun"] = true,
        });

        Assert.Contains("dryRun", text, StringComparison.Ordinal);
        Assert.Contains("files changed", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindDeadCode_FindsTheUnreferencedMember()
    {
        var text = await server.CallAsync("find_dead_code", []);

        Assert.Contains("findings", text, StringComparison.Ordinal);
        Assert.Contains("EXACT", text, StringComparison.Ordinal);
    }

    private static int Total(string response)
    {
        var digits = response[(response.IndexOf("total=", StringComparison.Ordinal) + "total=".Length)..]
            .TakeWhile(char.IsAsciiDigit)
            .ToArray();

        return int.Parse(digits, CultureInfo.InvariantCulture);
    }
}
