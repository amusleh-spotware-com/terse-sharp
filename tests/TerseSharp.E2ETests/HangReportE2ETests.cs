namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class HangReportE2ETests
{
    private static readonly string HangRoot =
        Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "HangSolution");

    [Fact]
    public async Task RunTests_WhenATestNeverFinishes_NamesThatTestInsteadOfAnsweringNothing()
    {
        var server = await TerseServerProcess.StartAsync(
            HangRoot,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(HangRoot, "HangSolution.slnx")],
            TestContext.Current.CancellationToken);

        try
        {
            var built = await CallAsync(server, "build", []);
            var text = await CallAsync(server, "run_tests", new() { ["project"] = "Hang.Tests", ["noBuild"] = true, ["timeoutSeconds"] = 40 });

            Assert.DoesNotContain("ERROR", built, StringComparison.Ordinal);
            Assert.DoesNotContain("PASSED", text, StringComparison.Ordinal);
            Assert.Contains("WARNING the run was stopped while these test(s) were still running", text, StringComparison.Ordinal);
            Assert.Contains("Hang.Tests.HangingTests.NeverFinishes", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static Task<string> CallAsync(TerseServerProcess server, string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);
}
