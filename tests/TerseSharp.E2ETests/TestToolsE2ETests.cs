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
        Assert.StartsWith("3 failures", text, StringComparison.Ordinal);
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
        Assert.StartsWith("ERROR ProjectNotFound", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
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

        Assert.StartsWith("7 tests", text, StringComparison.Ordinal);
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
            ["verbose"] = true,
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

        Assert.StartsWith("1 tests", text, StringComparison.Ordinal);
        Assert.Contains("DeliberateOutcomesTests.FailsAssertion", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliberateOutcomesTests.Throws", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_GreenRun_StaysTiny()
    {
        var text = ToolCensus.WithoutSteer(await RunAsync(new()
        {
            ["project"] = TestProject,
            ["test"] = "Fixture.Trading.Tests.DeliberateOutcomesTests.Succeeds",
        }));

        Assert.StartsWith("run_tests PASSED", text, StringComparison.Ordinal);
        Assert.Contains("passed=1 skipped=0 total=1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", text, StringComparison.Ordinal);
        Assert.True(Tokens(text) <= 40, text);
    }

    private Task<string> RunAsync(Dictionary<string, object?> arguments) => server.CallAsync("run_tests", arguments);

    private static int Tokens(string text) => (text.Length + 3) / 4;
    [Fact]
    public async Task RunTests_WithVerbose_KeepsTheFullReportOnAGreenRun()
    {
        var text = await RunAsync(new()
        {
            ["project"] = TestProject,
            ["test"] = "Fixture.Trading.Tests.DeliberateOutcomesTests.Succeeds",
            ["verbose"] = true,
        });

        Assert.Contains("0 failures", text, StringComparison.Ordinal);
        Assert.Contains("passed=1 failed=0 skipped=0 total=1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WithAProjectName_ResolvesItToTheProjectFile()
    {
        var text = await RunAsync(new()
        {
            ["project"] = "Fixture.Trading.Tests",
            ["test"] = "Fixture.Trading.Tests.DeliberateOutcomesTests.Succeeds",
        });

        Assert.StartsWith("run_tests PASSED", text, StringComparison.Ordinal);
        Assert.Contains("passed=1 skipped=0 total=1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MSB1009", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WithAnUnknownProjectName_NamesTheSolutionsProjectsInsteadOfFailingInsideMSBuild()
    {
        var text = await RunAsync(new() { ["project"] = "Fixture.Trading.Hosting" });

        Assert.StartsWith("ERROR ProjectNotFound", text, StringComparison.Ordinal);
        Assert.Contains("closest: Fixture.Trading", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MSB1009", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_WithAProjectNameFromTheSolution_ResolvesItThroughTheSolutionsProjectList()
    {
        Assert.False(
            Directory.Exists(Path.Combine(TerseServerFixture.FixtureRoot, "Fixture.Trading")),
            "the name must not resolve as a path, or the solution lookup is not what answered");

        var text = await server.CallAsync("build", new() { ["project"] = "Fixture.Trading" });

        Assert.StartsWith("build ok", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MSB1009", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListTests_WithAProjectName_ResolvesItToTheProjectFile()
    {
        var text = await server.CallAsync("list_tests", new() { ["project"] = "Fixture.Trading.Tests" });

        Assert.StartsWith("7 tests", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WhenTheOutputIsLocked_WarnsAndReportsTheRetryInsteadOfRawMSBuildOutput()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to overwrite an open file");

        var project = Path.Combine(TerseServerFixture.FixtureRoot, "tests", "Fixture.Trading.Tests");
        var output = Path.Combine(project, "bin", "Debug", "net10.0", "Fixture.Trading.Tests.dll");

        await RunAsync(new() { ["project"] = TestProject });

        Assert.True(File.Exists(output), output);

        File.SetLastWriteTimeUtc(Path.Combine(project, "DeliberateOutcomesTests.cs"), DateTime.UtcNow);

        using (new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var text = await RunAsync(new() { ["project"] = TestProject });

            Assert.Contains("WARNING a locked output file blocked the operation", text, StringComparison.Ordinal);
            Assert.Contains("NOTE the workspace was unloaded and the test run retried", text, StringComparison.Ordinal);
        }

        Assert.Contains("passed=3 failed=3 skipped=1 total=7", await RunAsync(new() { ["project"] = TestProject }), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WithChanged_WhenNoTestProjectDependsOnTheChange_RunsEverythingAndSaysWhy()
    {
        const string Probe = "src/Fixture.Trading/SelectionProbe.cs";

        await server.CallAsync("write_text", new()
        {
            ["path"] = Probe,
            ["content"] = "namespace Fixture.Trading;\n\npublic sealed record SelectionProbe(int Value);\n",
            ["force"] = true,
        });

        try
        {
            var text = await RunAsync(new() { ["changed"] = true });

            Assert.True(
                text.Contains("NOTE changed=true ran every test project - no test project depends", StringComparison.Ordinal),
                text);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true, ["force"] = true });
        }
    }

    [Fact]
    public async Task RunTests_WithChangedAndAnExplicitProject_RunsThatProjectWithoutSelecting()
    {
        var text = await RunAsync(new() { ["changed"] = true, ["project"] = TestProject });

        Assert.DoesNotContain("NOTE changed=true", text, StringComparison.Ordinal);
    }
}
