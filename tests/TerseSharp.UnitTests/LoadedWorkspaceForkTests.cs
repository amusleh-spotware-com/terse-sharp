using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class LoadedWorkspaceForkTests
{
    private static string GeneratorSolutionPath { get; } = Path.Combine(
        Fixtures.RepositoryRoot, "fixtures", "GeneratorSolution", "GeneratorSolution.slnx");

    [Fact]
    public async Task TryApplyAsync_AfterAnEdit_KeepsTheCompilationOfAProjectItDidNotTouch()
    {
        using var registry = new WorkspaceRegistry(watch: false);

        await registry.LoadAsync(GeneratorSolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;

        var workspace = lease.Workspace;
        var edited = workspace.Solution.Projects.Single(project => project.Name is "Fixture.Generated");
        var untouched = workspace.Solution.Projects.Single(project => project.Id != edited.Id);

        var before = await untouched.GetCompilationAsync(TestContext.Current.CancellationToken);
        var document = edited.Documents.First(entry => entry.Name is "Consumer.cs");
        var text = await document.GetTextAsync(TestContext.Current.CancellationToken);

        Assert.True(await workspace.TryApplyAsync(
            workspace.Solution.WithDocumentText(document.Id, text),
            [document.Id],
            TestContext.Current.CancellationToken));

        var after = await workspace.Solution
            .GetProject(untouched.Id)!
            .GetCompilationAsync(TestContext.Current.CancellationToken);

        Assert.Same(before, after);
    }
}
