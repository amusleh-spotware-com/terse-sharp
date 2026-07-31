namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class TestToolsE2ETests(TerseServerFixture server)
{
    private const string TestProject = "tests/Fixture.Trading.Tests/Fixture.Trading.Tests.csproj";

    [Fact]
    public async Task RunTests_OnAFailingProject_ReportsCountersFailuresAndFrames()
    {
        var text = await RunAsync(new() { ["project"] = TestProject });

        Assert.True(Tokens(text) <= 500, text);
        Assert.Contains("3 failures (truncated=false, total=3)", text, StringComparison.Ordinal);
        Assert.Contains("passed=3 failed=3 skipped=1 total=7", text, StringComparison.Ordinal);
        Assert.Contains("FAIL Fixture.Trading.Tests.DeliberateOutcomesTests.FailsAssertion", text, StringComparison.Ordinal);
        Assert.Contains("Expected: 4", text, StringComparison.Ordinal);
        Assert.Contains("Actual:   5", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException : probe boom", text, StringComparison.Ordinal);
        Assert.Contains("at tests/Fixture.Trading.Tests/DeliberateOutcomesTests.cs:39", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WithAnExactTestName_RunsThatTestAlone()
    {
        var text = await RunAsync(new()
        {
            ["project"] = TestProject,
            ["test"] = "Fixture.Trading.Tests.DeliberateOutcomesTests.FailsAssertion",
        });

        Assert.Contains("passed=0 failed=1 skipped=0 total=1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Throws", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WithAClassPrefix_RunsEveryTestOfThatClass()
    {
        var text = await RunAsync(new()
        {
            ["project"] = TestProject,
            ["test"] = "Fixture.Trading.Tests.DeliberateOutcomesTests",
        });

        Assert.Contains("total=7", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WithBothTestAndFilter_IsRefused()
    {
        var text = await RunAsync(new()
        {
            ["project"] = TestProject,
            ["test"] = "Fixture.Trading.Tests.DeliberateOutcomesTests.Passes",
            ["filter"] = "FullyQualifiedName~Passes",
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("test and filter cannot be combined", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WithAFilterThatMatchesNothing_WarnsInsteadOfLookingGreen()
    {
        var text = await RunAsync(new()
        {
            ["project"] = TestProject,
            ["filter"] = "FullyQualifiedName~NoSuchTestAnywhere",
        });

        Assert.Contains("total=0", text, StringComparison.Ordinal);
        Assert.Contains("WARNING no test matched filter", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_OnAMissingProject_DoesNotClaimZeroFailures()
    {
        var text = await RunAsync(new() { ["project"] = "tests/Nope/Nope.csproj" });

        Assert.DoesNotContain("0 failures", text, StringComparison.Ordinal);
        Assert.Contains("FAILED", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WithIncludePassedAndSlowest_ListsPassingAndSlowestTests()
    {
        var text = await RunAsync(new()
        {
            ["project"] = TestProject,
            ["includePassed"] = true,
            ["slowest"] = 2,
        });

        Assert.Contains("PASS Fixture.Trading.Tests.DeliberateOutcomesTests.Succeeds", text, StringComparison.Ordinal);
        Assert.Contains("SLOW Fixture.Trading.Tests.DeliberateOutcomesTests.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WithNoBuild_ReusesTheExistingBinaries()
    {
        await RunAsync(new() { ["project"] = TestProject });

        var text = await RunAsync(new() { ["project"] = TestProject, ["noBuild"] = true });

        Assert.Contains("passed=3 failed=3 skipped=1 total=7", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_OnASolutionWithoutTestProjects_IsNotReportedAsGreen()
    {
        var text = await RunAsync([]);

        Assert.True(
            text.Contains("WARNING", StringComparison.Ordinal) || text.Contains("FAILED", StringComparison.Ordinal),
            text);
    }

    [Fact]
    public async Task RunTests_ThatExceedsItsTimeout_SaysSoInsteadOfClaimingSuccess()
    {
        var text = await RunAsync(new() { ["project"] = TestProject, ["timeoutSeconds"] = 1 });

        Assert.Contains("FAILED timed out after", text, StringComparison.Ordinal);
        Assert.DoesNotContain("0 failures", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RerunFailed_AfterAFailingRun_RunsOnlyTheFailures()
    {
        await RunAsync(new() { ["project"] = TestProject });

        var text = await server.CallAsync("rerun_failed", new() { ["noBuild"] = true });

        Assert.Contains("passed=0 failed=3 skipped=0 total=3", text, StringComparison.Ordinal);
        Assert.Contains("FAIL Fixture.Trading.Tests.DeliberateOutcomesTests.FailsAssertion", text, StringComparison.Ordinal);
        Assert.Contains("FAIL Fixture.Trading.Tests.DeliberateOutcomesTests.Throws", text, StringComparison.Ordinal);
        Assert.Contains("FAIL Fixture.Trading.Tests.DeliberateOutcomesTests.FailsWithData(volume: 0)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListTests_OnATestProject_NamesEveryTestWithoutRunningThem()
    {
        var text = await server.CallAsync("list_tests", new() { ["project"] = TestProject });

        Assert.Contains("7 tests (truncated=false, total=7)", text, StringComparison.Ordinal);
        Assert.Contains("Fixture.Trading.Tests.DeliberateOutcomesTests.SkippedByDesign", text, StringComparison.Ordinal);
        Assert.Contains("Fixture.Trading.Tests.DeliberateOutcomesTests.PassesWithData(volume: 1)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("FAIL", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WithAParameterizedTestName_EscapesTheFilterAndRunsThatCase()
    {
        var text = await RunAsync(new()
        {
            ["project"] = TestProject,
            ["test"] = "Fixture.Trading.Tests.DeliberateOutcomesTests.PassesWithData(volume: 1)",
        });

        Assert.Contains("passed=2 failed=0 skipped=0 total=2", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListTests_WithContains_KeepsOnlyMatchingNames()
    {
        var text = await server.CallAsync("list_tests", new()
        {
            ["project"] = TestProject,
            ["contains"] = "FailsAssertion",
        });

        Assert.Contains("1 tests (truncated=false, total=1)", text, StringComparison.Ordinal);
        Assert.Contains("DeliberateOutcomesTests.FailsAssertion", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliberateOutcomesTests.Throws", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_GreenRun_StaysTiny()
    {
        var text = await RunAsync(new()
        {
            ["project"] = TestProject,
            ["test"] = "Fixture.Trading.Tests.DeliberateOutcomesTests.Succeeds",
        });

        Assert.Contains("0 failures", text, StringComparison.Ordinal);
        Assert.Contains("passed=1 failed=0 skipped=0 total=1", text, StringComparison.Ordinal);
        Assert.True(Tokens(text) <= 120, text);
    }

    private Task<string> RunAsync(Dictionary<string, object?> arguments) => server.CallAsync("run_tests", arguments);

    private static int Tokens(string text) => (text.Length + 3) / 4;
}
