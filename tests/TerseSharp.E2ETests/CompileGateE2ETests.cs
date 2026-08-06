namespace TerseSharp.E2ETests;

public sealed class CompileGateE2ETests : IAsyncLifetime
{
    private static readonly string BrokenRoot =
        Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "BrokenSolution");

    private static readonly string CalculatorPath =
        Path.Combine(BrokenRoot, "src", "Fixture.Broken", "Calculator.cs");

    private TerseServerProcess server = null!;
    private string original = null!;

    public async ValueTask InitializeAsync()
    {
        original = await File.ReadAllTextAsync(CalculatorPath);

        server = await TerseServerProcess.StartAsync(
            BrokenRoot,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--workspace", Path.Combine(BrokenRoot, "BrokenSolution.slnx")],
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await server.StopAsync();
        await File.WriteAllTextAsync(CalculatorPath, original);
    }

    [Fact]
    public async Task AnEdit_ReportsTheDiagnosticCountsAndTheDeltaItCaused()
    {
        var text = await CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Broken.Calculator.Healthy",
            ["body"] = "{ return 3; }",
            ["dryRun"] = true,
        });

        Assert.Contains("errors=1 (+0)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("would be rolled back", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADryRunThatWouldBreakTheBuild_SaysSoInsteadOfReportingAZeroDelta()
    {
        var text = await CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Broken.Calculator.Healthy",
            ["body"] = "{ return undefinedSymbol; }",
            ["dryRun"] = true,
        });

        Assert.Contains("would be rolled back", text, StringComparison.Ordinal);
        Assert.Contains("CS0103", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditingAValidMember_AppliesEvenThoughTheFileAlreadyHasAnUnrelatedError()
    {
        var text = await CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Broken.Calculator.Healthy",
            ["body"] = "{ var first = 1; var second = 2; return first + second; }",
            ["dryRun"] = false,
        });

        Assert.DoesNotContain("CompileRegression", text, StringComparison.Ordinal);
        Assert.Contains("changedLines=", text, StringComparison.Ordinal);

        var onDisk = await File.ReadAllTextAsync(CalculatorPath, TestContext.Current.CancellationToken);

        Assert.Contains("first + second", onDisk, StringComparison.Ordinal);
        Assert.Contains("this does not compile", onDisk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IntroducingANewError_IsStillRefusedAndRolledBack()
    {
        var before = await File.ReadAllTextAsync(CalculatorPath, TestContext.Current.CancellationToken);

        var text = await CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Broken.Calculator.Healthy",
            ["body"] = "{ return \"a brand new error\"; }",
            ["dryRun"] = false,
        });

        Assert.Contains("ERROR CompileRegression", text, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(CalculatorPath, TestContext.Current.CancellationToken));
    }

    private Task<string> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);
}
