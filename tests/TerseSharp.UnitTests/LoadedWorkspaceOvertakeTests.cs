using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class LoadedWorkspaceOvertakeTests
{
    [Fact]
    public async Task TryApplyAsync_WhenAnotherCallRewroteTheDocumentAfterThisCallRead_RefusesAndKeepsTheOtherEdit()
    {
        using var temporary = TemporarySolution.Create();
        using var registry = new WorkspaceRegistry(watch: false);

        await registry.LoadAsync(temporary.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;

        var workspace = lease.Workspace;
        var document = FirstDocument(workspace);

        workspace.MarkRead();

        var stale = await PrefixedAsync(workspace.Solution, document.Id, "// stale\n");

        Assert.True(await Task.Run(() => LandFirstAsync(workspace, document.Id), TestContext.Current.CancellationToken));
        Assert.False(await workspace.TryApplyAsync(stale, [document.Id], TestContext.Current.CancellationToken));

        var onDisk = await File.ReadAllTextAsync(document.FilePath!, TestContext.Current.CancellationToken);

        Assert.StartsWith("// landed first\n", onDisk, StringComparison.Ordinal);
        Assert.DoesNotContain("// stale", onDisk, StringComparison.Ordinal);
        Assert.Equal([document.FilePath!], workspace.TakeOvertaken());
    }

    [Fact]
    public async Task TryApplyAsync_ForTwoSuccessiveEditsOfOneCall_LandsBoth()
    {
        using var temporary = TemporarySolution.Create();
        using var registry = new WorkspaceRegistry(watch: false);

        await registry.LoadAsync(temporary.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;

        var workspace = lease.Workspace;
        var document = FirstDocument(workspace);

        workspace.MarkRead();

        Assert.True(await workspace.TryApplyAsync(await PrefixedAsync(workspace.Solution, document.Id, "// first\n"), [document.Id], TestContext.Current.CancellationToken));
        Assert.True(await workspace.TryApplyAsync(await PrefixedAsync(workspace.Solution, document.Id, "// second\n"), [document.Id], TestContext.Current.CancellationToken));

        var onDisk = await File.ReadAllTextAsync(document.FilePath!, TestContext.Current.CancellationToken);

        Assert.StartsWith("// second\n// first\n", onDisk, StringComparison.Ordinal);
    }

    private static Document FirstDocument(LoadedWorkspace workspace) =>
        workspace.Solution.Projects.Single().Documents.First(document => document.FilePath is { Length: > 0 });

    private static async Task<bool> LandFirstAsync(LoadedWorkspace workspace, DocumentId id)
    {
        workspace.MarkRead();

        var landed = await PrefixedAsync(workspace.Solution, id, "// landed first\n");

        return await workspace.TryApplyAsync(landed, [id], TestContext.Current.CancellationToken);
    }

    private static async Task<Solution> PrefixedAsync(Solution solution, DocumentId id, string prefix) =>
        solution.WithDocumentText(id, SourceText.From(prefix + await solution.GetDocument(id)!.GetTextAsync(TestContext.Current.CancellationToken)));
}
