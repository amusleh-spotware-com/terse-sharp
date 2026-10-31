namespace TerseSharp.E2ETests;

public sealed class SubsetSolutionE2ETests
{
    private const string Filter = """
        {
          "solution": {
            "path": "SelectionSolution.slnx",
            "projects": [
              "src\\Selection.Core\\Selection.Core.csproj"
            ]
          }
        }
        """;

    private static readonly string SelectionRoot =
        Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "SelectionSolution");

    private static readonly string FilterPath = Path.Combine(SelectionRoot, "SelectionSolution.Core.slnf");

    [Fact]
    public async Task ASubsetSolution_IsNamedByStatusUsagesAndAMissingDocument()
    {
        await File.WriteAllTextAsync(FilterPath, Filter, TestContext.Current.CancellationToken);

        var server = await StartAsync();

        try
        {
            var status = await CallAsync(server, "workspace_status", []);
            var usages = await CallAsync(server, "find_usages", new() { ["symbolId"] = "Adder.Add" });
            var missing = await CallAsync(server, "get_symbol_source", new() { ["symbolId"] = "AdderTests", ["path"] = "tests/Selection.Core.Tests/AdderTests.cs" });

            Assert.Contains("WARNING subset - SelectionSolution.slnx beside it holds 2 project(s) SelectionSolution.Core.slnf does not load (Selection.Core.Tests, Selection.Other.Tests)", status, StringComparison.Ordinal);
            Assert.Contains("NOTE incomplete - SelectionSolution.slnx beside it holds 2 project(s)", usages, StringComparison.Ordinal);
            Assert.Contains("exists on disk but is not a document of the loaded SelectionSolution.Core.slnf: it belongs to Selection.Core.Tests, which SelectionSolution.slnx beside it holds", missing, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
            File.Delete(FilterPath);
        }
    }

    [Fact]
    public async Task LoadWorkspace_WithReplace_UnloadsTheSubsetSoTheWiderSolutionAnswersAlone()
    {
        await File.WriteAllTextAsync(FilterPath, Filter, TestContext.Current.CancellationToken);

        var server = await StartAsync();

        try
        {
            var replaced = await CallAsync(server, "load_workspace", new() { ["path"] = Path.Combine(SelectionRoot, "SelectionSolution.slnx"), ["replace"] = true });
            var listed = await CallAsync(server, "list_workspaces", []);
            var usages = await CallAsync(server, "find_usages", new() { ["symbolId"] = "Adder.Add" });

            Assert.Contains("replace=true - unloaded SelectionSolution.Core.slnf", replaced, StringComparison.Ordinal);
            Assert.DoesNotContain("SelectionSolution.Core.slnf", listed, StringComparison.Ordinal);
            Assert.Contains("AdderTests.cs", usages, StringComparison.Ordinal);
            Assert.DoesNotContain("NOTE incomplete", usages, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
            File.Delete(FilterPath);
        }
    }

    private static Task<TerseServerProcess> StartAsync() => TerseServerProcess.StartAsync(
        SelectionRoot,
        [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", FilterPath],
        TestContext.Current.CancellationToken);

    private static Task<string> CallAsync(TerseServerProcess server, string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);
}
