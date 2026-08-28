using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class LoadedWorkspacePartialApplyTests
{
    [Fact]
    public async Task TryApplyAsync_WhenOneDocumentCannotBeWritten_AnswersFalseAndLeavesTheSolutionMatchingDisk()
    {
        using var temporary = TemporarySolution.Create();
        using var registry = new WorkspaceRegistry(watch: false);

        await registry.LoadAsync(temporary.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;

        var workspace = lease.Workspace;
        var project = workspace.Solution.Projects.Single();
        var first = project.Documents.First(document => document.FilePath is { Length: > 0 });
        var last = project.Documents.Last(document => document.FilePath is { Length: > 0 });

        Assert.NotEqual(first.Id, last.Id);

        var updated = workspace.Solution
            .WithDocumentText(first.Id, SourceText.From(await Rewritten(first)))
            .WithDocumentText(last.Id, SourceText.From(await Rewritten(last)));

        Unwritable(last.FilePath!);

        var applied = await workspace.TryApplyAsync(
            updated,
            [first.Id, last.Id],
            TestContext.Current.CancellationToken);

        var onDisk = await File.ReadAllTextAsync(first.FilePath!, TestContext.Current.CancellationToken);

        Assert.False(applied);
        Assert.Contains("// rewritten", onDisk, StringComparison.Ordinal);

        var known = await workspace.Solution
            .GetDocument(first.Id)!
            .GetTextAsync(TestContext.Current.CancellationToken);

        Assert.Equal(onDisk, known.ToString());
    }
    private static async Task<string> Rewritten(Document document) =>
        "// rewritten\n" + await document.GetTextAsync(TestContext.Current.CancellationToken);

    private static void Unwritable(string path)
    {
        File.Delete(path);
        Directory.CreateDirectory(path);
    }
}
