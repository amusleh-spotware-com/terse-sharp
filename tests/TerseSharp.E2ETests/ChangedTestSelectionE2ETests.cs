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
            await CallAsync(server, "build", []);
            await CallAsync(server, "load_workspace", new()
            {
                ["path"] = Path.Combine(SelectionRoot, "SelectionSolution.slnx"),
                ["reload"] = true,
            });

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

            var reloaded = await CallAsync(server, "load_workspace", new() { ["reload"] = true });

            Assert.DoesNotContain("ERROR", reloaded, StringComparison.Ordinal);

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

    [Fact]
    public async Task RunTests_WithAConcurrentBatchOfTwoProjects_MergesBothIntoTheVerdictParallelOneReaches()
    {
        var server = await StartAsync();

        try
        {
            var concurrent = await CallAsync(server, "run_tests", new()
            {
                ["projects"] = new[] { "Selection.Core.Tests", "Selection.Other.Tests" },
                ["timeoutSeconds"] = 600,
            });

            var serial = await CallAsync(server, "run_tests", new()
            {
                ["projects"] = new[] { "Selection.Core.Tests", "Selection.Other.Tests" },
                ["parallel"] = 1,
                ["timeoutSeconds"] = 600,
            });

            Assert.StartsWith("run_tests PASSED", concurrent, StringComparison.Ordinal);
            Assert.StartsWith("run_tests PASSED", serial, StringComparison.Ordinal);
            Assert.Contains("Selection.Core.Tests:1", concurrent, StringComparison.Ordinal);
            Assert.Contains("Selection.Other.Tests:1", concurrent, StringComparison.Ordinal);
            Assert.Equal("run_tests PASSED  passed=2 skipped=0 total=2 ", Verdict(concurrent));
            Assert.Equal(Verdict(serial), Verdict(concurrent));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static string Verdict(string response) =>
        response.IndexOf("durationMs=", StringComparison.Ordinal) is var marker && marker < 0
            ? response
            : response[..marker];

    [Fact]
    public async Task RunTests_WhenABatchsOwnBuildCannotFinish_NamesTheProjectAndNeverOffersNoBuild()
    {
        var server = await StartAsync();

        try
        {
            var text = await CallAsync(server, "run_tests", new()
            {
                ["projects"] = new[] { "Selection.Core.Tests", "Selection.Other.Tests" },
                ["timeoutSeconds"] = 1,
                ["properties"] = new[] { "TerseStallBuild=true" },
            });

            Assert.DoesNotContain("run_tests PASSED", text, StringComparison.Ordinal);
            Assert.Contains("timed out, so no project ran", text, StringComparison.Ordinal);
            Assert.Contains("raise timeoutSeconds", text, StringComparison.Ordinal);
            Assert.DoesNotContain("noBuild=true", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task RunTests_WithTheSameProjectTwiceInABatch_IsRefusedInsteadOfRacingTheAssemblyAgainstItself()
    {
        var server = await StartAsync();

        try
        {
            var text = await CallAsync(server, "run_tests", new()
            {
                ["projects"] = new[] { "Selection.Core.Tests", "Selection.Core.Tests" },
                ["timeoutSeconds"] = 600,
            });

            Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
            Assert.Contains("Selection.Core.Tests.csproj twice", text, StringComparison.Ordinal);
            Assert.Contains("remedy:", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task RunTests_OverASolution_RunsEachTestProjectAsItsOwnInvocationInsteadOfOneSolutionWideRun()
    {
        var server = await StartAsync();

        try
        {
            var single = await CallAsync(server, "run_tests", new() { ["verbose"] = true, ["parallel"] = 1, ["timeoutSeconds"] = 600 });

            await CallAsync(server, "load_workspace", new()
            {
                ["path"] = Path.Combine(SelectionRoot, "SelectionSolution.slnx"),
                ["reload"] = true,
            });

            var expanded = await CallAsync(server, "run_tests", new() { ["verbose"] = true, ["timeoutSeconds"] = 600 });

            Assert.Contains("dotnet test", CommandLine(single), StringComparison.Ordinal);
            Assert.Contains("SelectionSolution.slnx", CommandLine(single), StringComparison.Ordinal);
            Assert.Contains("Selection.Core.Tests.dll", CommandLine(expanded), StringComparison.Ordinal);
            Assert.Contains("Selection.Other.Tests.dll", CommandLine(expanded), StringComparison.Ordinal);
            Assert.DoesNotContain("dotnet test", CommandLine(expanded), StringComparison.Ordinal);
            Assert.DoesNotContain("SelectionSolution.slnx", CommandLine(expanded), StringComparison.Ordinal);
            Assert.Contains("total=2", expanded, StringComparison.Ordinal);
            Assert.Equal(Verdict(single), Verdict(expanded));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static string CommandLine(string response) =>
            response.Split('\n').SingleOrDefault(line => line.StartsWith("command: ", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the response carried no command line: " + response);

    [Fact]
    public async Task Build_OnceItHasRestoredInThisSession_SkipsTheRestoreOnTheNextBuild()
    {
        var server = await StartAsync();

        try
        {
            var first = await CallAsync(server, "build", new() { ["verbose"] = true });
            var second = await CallAsync(server, "build", new() { ["verbose"] = true });

            Assert.DoesNotContain("--no-restore", CommandLine(first), StringComparison.Ordinal);
            Assert.Contains("--no-restore", CommandLine(second), StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task ListProjects_WithProperties_AnswersEachProjectsOwnEvaluatedValue()
    {
        var server = await StartAsync();

        try
        {
            var text = await CallAsync(server, "list_projects", new() { ["properties"] = "IsPackable,TargetFramework" });

            Assert.Contains("3 projects", text, StringComparison.Ordinal);
            Assert.Contains("Selection.Core  C#", text, StringComparison.Ordinal);
            Assert.Contains("IsPackable=true", text, StringComparison.Ordinal);
            Assert.Contains("IsPackable=false", text, StringComparison.Ordinal);
            Assert.Contains("TargetFramework=net10.0", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }
}
