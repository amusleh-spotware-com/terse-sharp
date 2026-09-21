using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

[Collection(nameof(FixtureSolutionCollection))]
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

    [Fact]
    public void Rebound_ForAReferenceThatNamesAFileThatIsNotOnDisk_DropsIt()
    {
        using var workspace = new AdhocWorkspace();

        var missing = Path.Combine(Path.GetTempPath(), "terse-analyzer-not-on-disk.dll");
        var project = workspace.AddProject("Gap", LanguageNames.CSharp);
        var solution = project.Solution.AddAnalyzerReference(project.Id, new MissingAnalyzerReference(missing));

        var rebound = AnalyzerRebind.Rebound(solution, ShadowCopyAnalyzerLoader.Shared);

        Assert.Single(solution.GetProject(project.Id)!.AnalyzerReferences);
        Assert.Empty(rebound.GetProject(project.Id)!.AnalyzerReferences);
    }

    [Fact]
    public void Unresolved_ForAReferenceThatNamesAFileThatIsNotOnDisk_CountsItAndItsProject()
    {
        using var workspace = new AdhocWorkspace();

        var missing = Path.Combine(Path.GetTempPath(), "terse-analyzer-not-on-disk.dll");
        var project = workspace.AddProject("Gap", LanguageNames.CSharp);
        var solution = project.Solution.AddAnalyzerReference(project.Id, new MissingAnalyzerReference(missing));

        var unresolved = AnalyzerRebind.Unresolved(solution);

        Assert.True(unresolved.Any);
        Assert.Equal(1, unresolved.ProjectCount);
        Assert.Equal([missing], unresolved.Paths);
    }

    [Fact]
    public void Unresolved_ForASolutionWithNoAnalyzerReferences_IsNone()
    {
        using var workspace = new AdhocWorkspace();

        var project = workspace.AddProject("Bare", LanguageNames.CSharp);

        Assert.False(AnalyzerRebind.Unresolved(project.Solution).Any);
    }

    private sealed class MissingAnalyzerReference(string path) : AnalyzerReference
    {
        public override string FullPath => path;

        public override object Id => path;

        public override string Display => path;

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => [];

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => [];
    }
}
