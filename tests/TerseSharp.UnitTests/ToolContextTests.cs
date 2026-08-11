using TerseSharp.Core;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

[Collection(nameof(FixtureSolutionCollection))]
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

        var call = context.WithWorkspace(
            null,
            null,
            loaded => loaded.SolutionPath,
            cancellationToken: TestContext.Current.CancellationToken);

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

        var call = context.WithWorkspaceAsync(
            null,
            null,
            loaded => Task.FromResult(loaded.SolutionPath),
            cancellationToken: TestContext.Current.CancellationToken);

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

        var text = await context.WithWorkspace(
            null,
            null,
            loaded => loaded.SolutionPath,
            cancellationToken: TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task WithWorkspaceAsync_WhenTheSyncThrows_StillReleasesTheLease()
    {
        using var files = TemporarySolution.Create();
        using var registry = new WorkspaceRegistry(watch: false);
        using var context = new ToolContext(registry, readOnly: false);

        await registry.LoadAsync(files.SolutionPath, TestContext.Current.CancellationToken);

        var workspace = registry.All()[0];

        workspace.Sync.Notice(files.OrderServicePath);

        var text = await context.WithWorkspaceAsync(
            null,
            null,
            loaded => Task.FromResult(loaded.SolutionPath),
            cancellationToken: new CancellationToken(canceled: true));

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.True(registry.Unload(files.SolutionPath));
        Assert.Empty(workspace.Solution.Projects);
    }

    [Fact]
    public async Task MeasureAsync_OverALoadedWorkspace_SplitsThePerCallFloorIntoResolveSyncAndAction()
    {
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false);

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        var latency = await context.MeasureAsync(8, TestContext.Current.CancellationToken);

        Assert.Equal(8, latency.Calls);
        Assert.True(latency.ResolveMs is >= 0 and < 100, $"resolveMs={latency.ResolveMs}");
        Assert.True(latency.SyncMs is >= 0 and < 100, $"syncMs={latency.SyncMs}");
        Assert.True(latency.ActionMs >= 0, $"actionMs={latency.ActionMs}");
    }

    [Fact]
    public async Task MeasureAsync_WithNothingLoaded_AnswersZeroWithoutThrowing()
    {
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false);

        var latency = await context.MeasureAsync(3, TestContext.Current.CancellationToken);

        Assert.Equal(3, latency.Calls);
        Assert.Equal(0, latency.SyncMs);
        Assert.Equal(0, latency.ActionMs);
    }

    [Fact]
    public async Task MeasurePhasesAsync_OverALoadedWorkspace_TimesTheOutlineTheCompileGateAndTheGitSpawnSeparately()
    {
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false);

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        var phases = await context.MeasurePhasesAsync(TestContext.Current.CancellationToken);

        Assert.EndsWith(".cs", phases.Document, StringComparison.Ordinal);
        Assert.False(Path.IsPathRooted(phases.Document), phases.Document);
        Assert.True(phases.OutlineMs > 0, $"outlineMs={phases.OutlineMs}");
        Assert.True(phases.GateMs > 0, $"gateMs={phases.GateMs}");
        Assert.True(phases.DiffMs > 0, $"diffMs={phases.DiffMs}");
    }

    [Fact]
    public async Task MeasurePhasesAsync_WithNothingLoaded_AnswersZeroWithoutThrowing()
    {
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false);

        var phases = await context.MeasurePhasesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, phases.Document);
        Assert.Equal(0, phases.OutlineMs);
        Assert.Equal(0, phases.GateMs);
        Assert.Equal(0, phases.DiffMs);
    }

    [Fact]
    public async Task MeasurePhasesAsync_WithTwoWorkspacesLoaded_AnswersNothingRatherThanAnUnmeasuredZero()
    {
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false);

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        await registry.LoadAsync(Fixtures.RazorSolutionPath, TestContext.Current.CancellationToken);

        var phases = await context.MeasurePhasesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, phases.Document);
        Assert.Equal(0, phases.OutlineMs);
    }

    [Fact]
    public async Task AnnounceAsync_WhenTheServedFamiliesChanged_TellsTheClientTheToolListMoved()
    {
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false, new ToolSurface(null, MarkupDerived: true));
        var announced = 0;

        context.ToolsChanged = _ =>
        {
            announced++;

            return Task.CompletedTask;
        };

        await context.AnnounceAsync(default, TestContext.Current.CancellationToken);
        await context.AnnounceAsync(context.Served(), TestContext.Current.CancellationToken);

        Assert.Equal(1, announced);
    }

    [Fact]
    public async Task AnnounceAsync_WhenTheSurfaceIsNotDerivedFromTheWorkspace_SaysNothing()
    {
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false, new ToolSurface(null, MarkupDerived: false));
        var announced = 0;

        context.ToolsChanged = _ =>
        {
            announced++;

            return Task.CompletedTask;
        };

        await context.AnnounceAsync(default, TestContext.Current.CancellationToken);

        Assert.Equal(0, announced);
    }
}
