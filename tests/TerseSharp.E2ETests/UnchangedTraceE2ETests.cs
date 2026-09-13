namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class UnchangedTraceE2ETests
{
    [Fact]
    public async Task RunTests_UnderTheMemoTraceFlag_NamesTheMemoDecisionAndStillReplaysTheRepeat()
    {
        var server = await TerseServerProcess.StartAsync(
            TerseServerFixture.RepositoryRoot,
            [
                TerseServerFixture.ServerAssemblyPath(),
                "serve",
                "--workspace",
                Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"),
            ],
            new Dictionary<string, string> { ["TERSE_UNCHANGED_TRACE"] = "1" },
            TestContext.Current.CancellationToken);

        try
        {
            var arguments = new Dictionary<string, object?>
            {
                ["project"] = "tests/Fixture.Trading.Tests/Fixture.Trading.Tests.csproj",
                ["test"] = "Fixture.Trading.Tests.DeliberateOutcomesTests.Passes",
                ["timeoutSeconds"] = 400,
            };

            var first = await server.CallAsync("run_tests", new(arguments), TestContext.Current.CancellationToken);
            var second = await server.CallAsync("run_tests", new(arguments), TestContext.Current.CancellationToken);

            Assert.StartsWith("run_tests PASSED", first, StringComparison.Ordinal);
            Assert.EndsWith("memo: miss - first run of this key; remembered", first, StringComparison.Ordinal);
            Assert.StartsWith("run_tests UNCHANGED", second, StringComparison.Ordinal);
            Assert.DoesNotContain("memo:", second, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }
}
