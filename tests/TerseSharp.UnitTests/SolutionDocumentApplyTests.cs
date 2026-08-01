using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class SolutionDocumentApplyTests
{
    private const string SpikeSource = "namespace Fixture.Trading;\n\npublic sealed class SpikeType\n{\n}\n";

    [Fact]
    public async Task CanApplyChange_ForDocumentAddAndRemove_AdvertisesSupportItCannotHonour()
    {
        using var solution = TemporarySolution.Create();
        using var workspace = await OpenAsync(solution);

        Assert.True(workspace.CanApplyChange(ApplyChangesKind.ChangeDocument));
        Assert.True(workspace.CanApplyChange(ApplyChangesKind.AddDocument));
        Assert.True(workspace.CanApplyChange(ApplyChangesKind.RemoveDocument));
    }

    [Fact]
    public async Task TryApplyChanges_AddingADocument_InjectsACompileItemIntoAnSdkStyleProject()
    {
        using var solution = TemporarySolution.Create();
        using var workspace = await OpenAsync(solution);

        var path = Path.Combine(solution.ProjectDirectory, "SpikeType.cs");
        var project = workspace.CurrentSolution.Projects.Single();
        var added = project.AddDocument("SpikeType.cs", SpikeSource, filePath: path);

        Assert.True(workspace.TryApplyChanges(added.Project.Solution));

        var rewritten = await File.ReadAllTextAsync(solution.ProjectPath, TestContext.Current.CancellationToken);

        Assert.Equal(SpikeSource, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Contains("<Compile Include=", rewritten, StringComparison.Ordinal);
        Assert.Contains("SpikeType.cs", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryApplyChanges_RemovingADocument_ThrowsBecauseTheItemComesFromAnSdkGlob()
    {
        using var solution = TemporarySolution.Create();
        using var workspace = await OpenAsync(solution);

        var document = workspace.CurrentSolution.Projects.Single().Documents.Single(entry => entry.Name is "Awkward.cs");
        var before = await File.ReadAllBytesAsync(solution.ProjectPath, TestContext.Current.CancellationToken);
        var removed = workspace.CurrentSolution.RemoveDocument(document.Id);

        Assert.ThrowsAny<Exception>(() => workspace.TryApplyChanges(removed));
        Assert.True(File.Exists(document.FilePath));
        Assert.Equal(before, await File.ReadAllBytesAsync(solution.ProjectPath, TestContext.Current.CancellationToken));
    }

    private static async Task<MSBuildWorkspace> OpenAsync(TemporarySolution solution)
    {
        MsBuildBootstrap.Ensure();

        var workspace = MSBuildWorkspace.Create();

        workspace.SkipUnrecognizedProjects = true;

        await workspace.OpenSolutionAsync(solution.SolutionPath, cancellationToken: TestContext.Current.CancellationToken);

        return workspace;
    }
}
