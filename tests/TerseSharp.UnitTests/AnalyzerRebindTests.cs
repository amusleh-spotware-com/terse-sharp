using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class AnalyzerRebindTests
{
    [Fact]
    public async Task Rebound_ForASolutionWithAnalyzerReferences_BindsEveryFileReferenceToTheGivenLoader()
    {
        using var registry = new WorkspaceRegistry(watch: false);

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;

        var references = FileReferences(AnalyzerRebind.Rebound(lease.Workspace.Solution, ShadowCopyAnalyzerLoader.Shared));

        Assert.NotEmpty(references);
        Assert.All(references, reference => Assert.Same(ShadowCopyAnalyzerLoader.Shared, reference.AssemblyLoader));
    }

    [Fact]
    public void Rebound_ForASolutionWithoutAnalyzerReferences_ReturnsTheSameInstance()
    {
        using var workspace = new AdhocWorkspace();

        var project = workspace.AddProject("Bare", LanguageNames.CSharp);

        Assert.Empty(project.AnalyzerReferences);

        var solution = project.Solution;

        Assert.Same(solution, AnalyzerRebind.Rebound(solution, ShadowCopyAnalyzerLoader.Shared));
    }

    private static AnalyzerFileReference[] FileReferences(Solution solution) =>
        [.. solution.Projects.SelectMany(project => project.AnalyzerReferences).OfType<AnalyzerFileReference>()];
}
