using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.UnitTests;

public sealed class LoadedWorkspaceUndoTests
{
    [Fact]
    public async Task Undo_WithNoHistory_SaysThereIsNothingToUndo()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        Assert.Equal("nothing to undo", loaded.Workspace.Undo());
    }

    [Fact]
    public async Task Undo_AfterAnEdit_RevertsIt()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);
        var before = await TextOfAsync(loaded);

        await EditAsync(loaded, "// Edited\n");

        Assert.Equal("reverted the last change", loaded.Workspace.Undo());
        Assert.Equal(before, await TextOfAsync(loaded));
    }

    [Fact]
    public async Task Undo_AfterAnExternalChangeToASnapshotPath_RefusesAndNamesThePath()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);
        var path = await EditAsync(loaded, "// Edited\n");

        loaded.Workspace.DropSnapshots([path]);

        var message = loaded.Workspace.Undo();

        Assert.Contains("nothing to undo", message, StringComparison.Ordinal);
        Assert.Contains("1 snapshot(s)", message, StringComparison.Ordinal);
        Assert.Contains("OrderService.cs", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DropSnapshots_ForAnUnrelatedPath_KeepsTheHistory()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await EditAsync(loaded, "// Edited\n");
        loaded.Workspace.DropSnapshots([Path.Combine(loaded.Files.ProjectDirectory, "Awkward.cs")]);

        Assert.Equal("reverted the last change", loaded.Workspace.Undo());
    }

    [Fact]
    public async Task DropSnapshots_ForAnEarlierSnapshotPath_DropsEverythingAboveItToo()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);
        var path = await EditAsync(loaded, "// First\n");

        await EditAsync(loaded, "// Second\n");
        loaded.Workspace.DropSnapshots([path]);

        Assert.Contains("2 snapshot(s)", loaded.Workspace.Undo(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Undo_AfterAReload_ReportsTheClearedStackWithoutThrowing()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await EditAsync(loaded, "// Edited\n");
        await loaded.ReloadAsync(TestContext.Current.CancellationToken);

        var message = loaded.Workspace.Undo();

        Assert.Contains("nothing to undo", message, StringComparison.Ordinal);
        Assert.Contains("the workspace reloaded", message, StringComparison.Ordinal);
    }

    private static async Task<string> EditAsync(TemporaryWorkspace loaded, string addition)
    {
        var document = loaded.Document("OrderService.cs");
        var text = await document.GetTextAsync(TestContext.Current.CancellationToken);
        var updated = loaded.Workspace.Solution.WithDocumentText(
            document.Id,
            SourceText.From(text.ToString() + addition, text.Encoding));

        Assert.True(await loaded.Workspace.TryApplyAsync(
            updated,
            [document.Id],
            TestContext.Current.CancellationToken));

        return document.FilePath!;
    }

    private static async Task<string> TextOfAsync(TemporaryWorkspace loaded)
    {
        var text = await loaded.Document("OrderService.cs").GetTextAsync(TestContext.Current.CancellationToken);

        return text.ToString();
    }
}
