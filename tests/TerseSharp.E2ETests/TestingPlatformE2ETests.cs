namespace TerseSharp.E2ETests;

[Collection(nameof(MtpSolutionCollection))]
public sealed class TestingPlatformE2ETests
{
    [Fact]
    public async Task RunTests_OnATestingPlatformSolution_ReportsTheCountersAndThenRerunsOnlyTheFailure()
    {
        var server = await StartedAsync();

        try
        {
            var text = await CallAsync(server, "run_tests", new() { ["timeoutSeconds"] = 300 });

            Assert.Contains("passed=3 failed=1 skipped=1 total=5", text, StringComparison.Ordinal);
            Assert.Contains("Mtp.Trading.Tests.DeliberateMtpOutcomesTests.FailsAssertion", text, StringComparison.Ordinal);
            Assert.Contains("Assert.Equal() Failure", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Zero tests ran", text, StringComparison.Ordinal);
            Assert.DoesNotContain("timed out", text, StringComparison.Ordinal);

            var rerun = await CallAsync(server, "rerun_failed", new() { ["timeoutSeconds"] = 300 });

            Assert.Contains("total=1", rerun, StringComparison.Ordinal);
            Assert.Contains("FailsAssertion", rerun, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task RunTests_OnATestingPlatformSolutionWithATestSelector_RunsOnlyThatClass()
    {
        var server = await StartedAsync();

        try
        {
            var text = await CallAsync(server, "run_tests", new()
            {
                ["test"] = "Mtp.Trading.Tests.LedgerTests",
                ["timeoutSeconds"] = 300,
            });

            Assert.StartsWith("run_tests PASSED", text, StringComparison.Ordinal);
            Assert.Contains("total=3", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task ListTests_UnderTheTestingPlatformRunner_ListsTheNamesByRunningTheTestModuleItself()
    {
        var server = await StartedAsync();

        try
        {
            var text = await CallAsync(server, "list_tests", []);

            Assert.Contains("5 tests", text, StringComparison.Ordinal);
            Assert.Contains("Mtp.Trading.Tests.LedgerTests.Balance_SubtractsTheDebitsFromTheCredits", text, StringComparison.Ordinal);
            Assert.Contains("Mtp.Trading.Tests.DeliberateMtpOutcomesTests.SkippedByDesign", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task ListTests_UnderTheTestingPlatformRunnerWithContains_KeepsOnlyTheMatchingNames()
    {
        var server = await StartedAsync();

        try
        {
            var text = await CallAsync(server, "list_tests", new() { ["contains"] = "LedgerTests" });

            Assert.Contains("3 tests", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DeliberateMtpOutcomesTests", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static Task<TerseServerProcess> StartedAsync()
    {
        var root = Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "MtpSolution");

        return TerseServerProcess.StartAsync(
            root,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(root, "MtpSolution.slnx")],
            TestContext.Current.CancellationToken);
    }

    private static Task<string> CallAsync(TerseServerProcess server, string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);

    [Fact]
    public async Task RunTests_OnATestingPlatformSolutionWhereTheSelectorMatchesNothing_IsAWarningRatherThanAGreenRun()
    {
        var server = await StartedAsync();

        try
        {
            var text = await CallAsync(server, "run_tests", new()
            {
                ["test"] = "Nope.NoSuchClass",
                ["timeoutSeconds"] = 300,
            });

            Assert.Contains("total=0", text, StringComparison.Ordinal);
            Assert.Contains("this is not a green run", text, StringComparison.Ordinal);
            Assert.DoesNotContain("run_tests PASSED", text, StringComparison.Ordinal);
            Assert.DoesNotContain("no test results were produced", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task RunTests_OnATestingPlatformSolutionWithRunSettings_RefusesInsteadOfLettingTheRunnerRejectTheSession()
    {
        var server = await StartedAsync();

        try
        {
            var text = await CallAsync(server, "run_tests", new()
            {
                ["runSettings"] = new[] { "xUnit.MaxParallelThreads=1" },
                ["timeoutSeconds"] = 300,
            });

            Assert.StartsWith("ERROR UnsupportedRunner", text, StringComparison.Ordinal);
            Assert.Contains("runSettings=", text, StringComparison.Ordinal);
            Assert.Contains("remedy:", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task RunTests_OnATestingPlatformSolutionWithAFilterXunitCannotSelect_RefusesInsteadOfMatchingNothing()
    {
        var server = await StartedAsync();

        try
        {
            var text = await CallAsync(server, "run_tests", new()
            {
                ["filter"] = "Category=Smoke",
                ["timeoutSeconds"] = 300,
            });

            Assert.StartsWith("ERROR UnsupportedRunner", text, StringComparison.Ordinal);
            Assert.Contains("test=", text, StringComparison.Ordinal);
            Assert.Contains("remedy:", text, StringComparison.Ordinal);
            Assert.DoesNotContain("no test matched filter", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }
}
