namespace TerseSharp.E2ETests;

public sealed class ChangedTestSelectionE2ETests
{
    private static readonly string SelectionRoot =
        Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "SelectionSolution");

    private static readonly string AdderPath =
        Path.Combine(SelectionRoot, "src", "Selection.Core", "Adder.cs");

    [Fact]
    public async Task RunTests_WithChanged_RunsOnlyTheTestProjectThatDependsOnTheChangeAndNamesTheSkippedOne()
    {
        var before = await File.ReadAllTextAsync(AdderPath, TestContext.Current.CancellationToken);
        var server = await StartAsync();

        try
        {
            var edit = await CallAsync(server, "replace_symbol_body", new()
            {
                ["symbolId"] = "M:Selection.Core.Adder.Add(System.Int32,System.Int32)",
                ["body"] = "=> right + left;",
            });

            var text = await CallAsync(server, "run_tests", new() { ["changed"] = true, ["timeoutSeconds"] = 600 });

            Assert.DoesNotContain("ERROR", edit, StringComparison.Ordinal);
            Assert.Contains("Selection.Core.Tests", text, StringComparison.Ordinal);
            Assert.Contains("Selection.Other.Tests", text, StringComparison.Ordinal);
            Assert.Contains("skipped", text, StringComparison.Ordinal);
            Assert.Contains("PASSED", text, StringComparison.Ordinal);
            Assert.Contains("total=1", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
            await File.WriteAllTextAsync(AdderPath, before, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task RunTests_WithChangedAndNothingModified_RunsEveryTestProjectAndSaysWhy()
    {
        var server = await StartAsync();

        try
        {
            var text = await CallAsync(server, "run_tests", new() { ["changed"] = true, ["timeoutSeconds"] = 600 });

            Assert.Contains("total=2", text, StringComparison.Ordinal);
            Assert.Contains("no document", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static Task<TerseServerProcess> StartAsync() => TerseServerProcess.StartAsync(
        SelectionRoot,
        [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(SelectionRoot, "SelectionSolution.slnx")],
        TestContext.Current.CancellationToken);

    private static Task<string> CallAsync(TerseServerProcess server, string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ImpactOf_WithTests_NamesTheTestClassThatReferencesTheSymbolAsAReadyRunTestsArgument()
    {
        var server = await StartAsync();

        try
        {
            var built = await CallAsync(server, "build", []);

            Assert.DoesNotContain("ERROR", built, StringComparison.Ordinal);

            var without = await CallAsync(server, "impact_of", new() { ["symbolId"] = "Adder.Add" });
            var with = await CallAsync(server, "impact_of", new() { ["symbolId"] = "Adder.Add", ["tests"] = true });

            Assert.Contains("(test=2)", without, StringComparison.Ordinal);
            Assert.DoesNotContain("run_tests test=", without, StringComparison.Ordinal);
            Assert.Contains("tests: run_tests test=AdderTests", with, StringComparison.Ordinal);
            Assert.DoesNotContain("run_tests test=StandaloneTests", with, StringComparison.Ordinal);
            Assert.DoesNotContain("run_tests test=AdderProbe", with, StringComparison.Ordinal);
            Assert.Contains("AdderProbe.cs", with, StringComparison.Ordinal);
            Assert.Contains("HEURISTIC", with, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }
}
