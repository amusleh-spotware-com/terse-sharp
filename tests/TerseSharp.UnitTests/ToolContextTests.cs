using TerseSharp.Core;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ToolContextTests
{
    [Fact]
    public void ReadyAsync_WithoutAPreload_IsAlreadyComplete()
    {
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false);

        Assert.True(context.ReadyAsync().IsCompleted);
    }

    [Fact]
    public async Task WithWorkspace_WhileThePreloadIsRunning_WaitsInsteadOfReportingNotLoaded()
    {
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false);
        var preload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        context.Preload(preload.Task);

        var call = context.WithWorkspace(null, null, loaded => loaded.SolutionPath);

        Assert.False(call.IsCompleted);

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        preload.SetResult();

        Assert.Equal(Fixtures.SolutionPath, await call);
    }

    [Fact]
    public async Task WithWorkspaceAsync_WhileThePreloadIsRunning_WaitsInsteadOfReportingNotLoaded()
    {
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false);
        var preload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        context.Preload(preload.Task);

        var call = context.WithWorkspaceAsync(null, null, loaded => Task.FromResult(loaded.SolutionPath));

        Assert.False(call.IsCompleted);

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        preload.SetResult();

        Assert.Equal(Fixtures.SolutionPath, await call);
    }

    [Fact]
    public async Task WithWorkspace_WhenThePreloadFailed_ReportsNotLoadedRatherThanTheException()
    {
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false);

        context.Preload(Task.FromException(new InvalidOperationException("the solution exploded")));

        var text = await context.WithWorkspace(null, null, loaded => loaded.SolutionPath);

        Assert.Contains("WorkspaceNotLoaded", text, StringComparison.Ordinal);
        Assert.Contains("the solution exploded", context.PreloadFailure!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadyAsync_WhenThePreloadIsCancelled_CompletesWithoutThrowing()
    {
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false);

        context.Preload(Task.FromCanceled(new CancellationToken(canceled: true)));

        await context.ReadyAsync();

        Assert.Equal("the workspace preload was cancelled", context.PreloadFailure);
    }
}
