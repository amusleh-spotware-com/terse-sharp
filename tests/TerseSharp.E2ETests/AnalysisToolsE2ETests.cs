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
    public async Task Analyze_ReportsDeadCodeAsPartOfItsResult()
    {
        var text = await server.CallAsync("analyze", new() { ["minSeverity"] = "info" });

        Assert.Contains("engines=compiler", text, StringComparison.Ordinal);
        Assert.Contains("dead-code", text, StringComparison.Ordinal);
        Assert.Contains("TERSE001", text, StringComparison.Ordinal);
        Assert.Contains("is never referenced", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_WithDeadCodeDisabled_OmitsTheScanAndTheFindings()
    {
        var text = await server.CallAsync("analyze", new()
        {
            ["minSeverity"] = "info",
            ["includeDeadCode"] = false,
        });

        Assert.DoesNotContain("dead-code", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TERSE001", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_FilteredToDeadCodeOnly_ReturnsOnlyThose()
    {
        var text = await server.CallAsync("analyze", new() { ["ids"] = "TERSE001", ["minSeverity"] = "info" });

        var records = text.Split('\n').Where(line => line.Contains(": ", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(records);
        Assert.All(records, line => Assert.StartsWith("TERSE001", line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Analyze_ScopedToAFile_ExcludesDeadCodeFoundInOtherFiles()
    {
        var text = await server.CallAsync("analyze", new()
        {
            ["path"] = "src/Fixture.Trading/Order.cs",
            ["minSeverity"] = "info",
        });

        Assert.DoesNotContain("NeverCalled", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderService.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_ScopedToTheFileHoldingIt_StillReportsThatDeadCode()
    {
        var text = await server.CallAsync("analyze", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["minSeverity"] = "info",
        });

        Assert.Contains("NeverCalled", text, StringComparison.Ordinal);
        Assert.Contains("TERSE001", text, StringComparison.Ordinal);
    }

    private static int Total(string response)
    {
        var newline = response.IndexOf('\n');
        var summary = response.AsSpan(0, newline < 0 ? response.Length : newline);
        var slash = summary.IndexOf('/');
        var counted = slash < 0 ? summary : summary[(slash + 1)..];

        return int.Parse(counted[..counted.IndexOf(' ')], CultureInfo.InvariantCulture);
    }
}
