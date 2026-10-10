using System.Diagnostics;
using TerseSharp.Core;
using TerseSharp.Server;
using TerseSharp.Server.Tools;

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
    public async Task ServedAsync_WhileThePreloadIsRunning_AnswersTheFileScanWithoutWaitingForTheLoad()
    {
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false, new ToolSurface(null, MarkupDerived: true));
        var preload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanned = new WorkspaceMarkup(Xaml: true, Razor: false, Resx: false);

        context.Preload(preload.Task, Task.FromResult(scanned), TestContext.Current.CancellationToken);

        var served = context.ServedAsync(TestContext.Current.CancellationToken);

        Assert.True(served.IsCompleted, "tools/list waited for the workspace load instead of answering from the scan");
        Assert.Equal(scanned, await served);

        preload.SetResult();
    }

    [Fact]
    public async Task Preload_WhenTheLoadedSurfaceDiffersFromTheScan_AnnouncesTheToolListMoved()
    {
        var announced = await AnnouncedAfterPreloadAsync(scanned: default);

        Assert.Equal(1, announced);
    }

    [Fact]
    public async Task Preload_WhenTheLoadedSurfaceMatchesTheScan_AnnouncesNothing()
    {
        var announced = await AnnouncedAfterPreloadAsync(scanned: WorkspaceMarkup.Every);

        Assert.Equal(0, announced);
    }

    private static async Task<int> AnnouncedAfterPreloadAsync(WorkspaceMarkup scanned)
    {
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false, new ToolSurface(null, MarkupDerived: true));
        var announced = 0;

        context.ToolsChanged = _ =>
        {
            announced++;

            return Task.CompletedTask;
        };

        context.Preload(Task.CompletedTask, Task.FromResult(scanned), TestContext.Current.CancellationToken);

        await context.Announcement;

        return announced;
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

    [Fact]
    public async Task WithWorkspaceAsync_OnTheFirstSemanticCallAfterADrop_WarmsEveryProjectOnceItHasAnswered()
    {
        using var registry = new WorkspaceRegistry { Pressured = static () => false };
        using var context = new ToolContext(registry, readOnly: false);

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        Assert.Equal(1, registry.DropIdleCompilations(TimeSpan.FromTicks(1), 0));

        var workspace = registry.All()[0];
        var answer = await context.WithWorkspaceAsync(null, null, static _ => Task.FromResult("answered"), cancellationToken: TestContext.Current.CancellationToken);
        await workspace.Warming;

        Assert.Equal("answered", answer);
        Assert.Equal(workspace.Solution.ProjectIds.Count, LoadedWorkspace.RealizedProjects(workspace.Solution));
    }

    [Fact]
    public async Task WithWorkspaceAsync_AfterADropUnderMemoryPressure_AnswersWithoutWarming()
    {
        using var registry = new WorkspaceRegistry { Pressured = static () => true };
        using var context = new ToolContext(registry, readOnly: false);

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        registry.DropIdleCompilations(TimeSpan.FromTicks(1), 0);

        var workspace = registry.All()[0];
        var answer = await context.WithWorkspaceAsync(null, null, static _ => Task.FromResult("answered"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("answered", answer);
        Assert.Same(Task.CompletedTask, workspace.Warming);
        Assert.Equal(0, LoadedWorkspace.RealizedProjects(workspace.Solution));
    }

    [Fact]
    public async Task RootOnlyTools_WhileThePreloadIsRunning_AnswerWithoutWaitingAndAsTheLoadedWorkspaceWould()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var solution = await TemporarySolution.CreateRepositoryAsync(cancellationToken);
        await File.AppendAllTextAsync(solution.OrderServicePath, "// I748" + Environment.NewLine, cancellationToken);
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false);
        var preload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        context.Preload(preload.Task, solution.SolutionPath);

        var early = await RootOnlyAnswersAsync(context, cancellationToken).WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);

        await registry.LoadAsync(solution.SolutionPath, cancellationToken);
        preload.SetResult();

        Assert.Equal(await RootOnlyAnswersAsync(context, cancellationToken), early);
        Assert.All(early, answer => Assert.False(answer.StartsWith("ERROR", StringComparison.Ordinal), answer));
        Assert.Contains("OrderService.cs", early[3], StringComparison.Ordinal);
    }

    private static Task<string[]> RootOnlyAnswersAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var files = new FileTools(context);
        var git = new GitTools(context, new ListingMemo());

        return Task.WhenAll(
            files.FindFiles("**/*.cs", cancellationToken: cancellationToken),
            files.SearchText("OrderService", "**/*.cs", workspace: "FixtureSolution", containers: true, cancellationToken: cancellationToken),
            git.History(maxResults: 5, cancellationToken: cancellationToken),
            git.ChangedFiles(cancellationToken: cancellationToken),
            git.DiffText(maxLines: 50, cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task WorkspaceStatus_WhileThePreloadIsRunning_JudgesAGuardAtOnceAndWaitsForTheStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false);
        var status = new WorkspaceTools(context, new ReplayGate(context, new UnchangedRun()));
        var preload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        context.Preload(preload.Task, Fixtures.SolutionPath);

        var judged = await status.WorkspaceStatus(guard: "grep -rn TODO src", cancellationToken: cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        var waiting = status.WorkspaceStatus(cancellationToken: cancellationToken);
        var pending = !waiting.IsCompleted;

        await registry.LoadAsync(Fixtures.SolutionPath, cancellationToken);
        preload.SetResult();

        Assert.StartsWith("guard ", judged, StringComparison.Ordinal);
        Assert.True(pending);
        Assert.Contains("FixtureSolution", await waiting, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithRootAsync_WhenAnotherWorkspaceIsNamedOrThePreloadFinished_TakesTheLoadedPath()
    {
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false);
        var preload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        context.Preload(preload.Task, Fixtures.SolutionPath);

        var named = await context.WithRootAsync("SomeOtherSolution", root => Task.FromResult(root), () => Task.FromResult("loaded"));

        preload.SetResult();
        await context.ReadyAsync();

        Assert.Equal("loaded", named);
        Assert.Equal("loaded", await context.WithRootAsync(null, root => Task.FromResult(root), () => Task.FromResult("loaded")));
    }

    [Fact]
    public void Queued_ForAShortWait_LeavesTheAnswerAlone()
    {
        var holder = new ActiveRun("run_tests", "", Stopwatch.GetTimestamp());

        var answer = ToolContext.Queued("3 symbols", TimeSpan.FromMilliseconds(1999), holder);

        Assert.Equal("3 symbols", answer);
    }

    [Fact]
    public void Queued_WhileABuildOrTestRunIsInFlight_NamesItAndItsAge()
    {
        var holder = new ActiveRun("run_tests", "", Stopwatch.GetTimestamp() - (Stopwatch.Frequency * 38));

        var answer = ToolContext.Queued("3 symbols", TimeSpan.FromMilliseconds(4200), holder);

        Assert.Equal("3 symbols\nNOTE queued 4200ms before this call ran, while run_tests was in flight on this solution for 38s", answer);
    }

    [Fact]
    public void Queued_WithNothingInFlight_LeavesTheAnswerAloneBecauseTheLoadIsReportedElsewhere()
    {
        var answer = ToolContext.Queued("3 symbols", TimeSpan.FromMilliseconds(2500), null);

        Assert.Equal("3 symbols", answer);
    }

    [Fact]
    public void Queued_ForARefusal_NeverAppendsANote()
    {
        var answer = ToolContext.Queued("ERROR SymbolNotFound: x", TimeSpan.FromSeconds(9), null);

        Assert.Equal("ERROR SymbolNotFound: x", answer);
    }

    [Theory]
    [InlineData("3 diagnostics\nNOTE queued 4200ms before this call ran, while build was in flight on this solution for 9s", "3 diagnostics")]
    [InlineData("3 diagnostics\nNOTE queued 4200ms before this call ran, while build was in flight on this solution for 9s\nnext: gate", "3 diagnostics\nnext: gate")]
    [InlineData("3 diagnostics\nnext: gate", "3 diagnostics\nnext: gate")]
    public void Unqueued_DropsOnlyTheQueuedLine_SoAReplayNeverRepeatsAWaitThatIsOver(string answer, string remembered) => Assert.Equal(remembered, ToolContext.Unqueued(answer));

    [Fact]
    public async Task ReadsAndListings_WhileThePreloadIsRunning_AnswerWithoutWaitingAndAsTheLoadedWorkspaceWould()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false);
        var preload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        context.Preload(preload.Task, Fixtures.SolutionPath);

        var early = await ReadAndListAnswersAsync(context, cancellationToken).WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);

        await registry.LoadAsync(Fixtures.SolutionPath, cancellationToken);
        preload.SetResult();

        Assert.Equal(await ReadAndListAnswersAsync(context, cancellationToken), early);
        Assert.All(early, answer => Assert.False(answer.StartsWith("ERROR", StringComparison.Ordinal), answer));
        Assert.Contains("OrderService.cs", early[3], StringComparison.Ordinal);
        Assert.Contains("OrderService.cs", early[5], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_OfAnAbsoluteCsPathWhileThePreloadIsRunning_AnswersTheOutlineTheLoadedWorkspaceWould()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var registry = new WorkspaceRegistry();
        using var context = new ToolContext(registry, readOnly: false);
        var preload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        context.Preload(preload.Task, Fixtures.SolutionPath);

        var pending = new FileTools(context).ReadText(Fixtures.OrderServicePath, cancellationToken: cancellationToken);

        await registry.LoadAsync(Fixtures.SolutionPath, cancellationToken);
        preload.SetResult();

        var answer = await pending.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);

        Assert.Contains("OrderService.Submit", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("outside-workspace", answer, StringComparison.Ordinal);
    }

    private static Task<string[]> ReadAndListAnswersAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var files = new FileTools(context);

        return Task.WhenAll(
            files.ReadText("notes.md", cancellationToken: cancellationToken),
            files.ReadText(paths: ["appsettings.json", "global.json"], cancellationToken: cancellationToken),
            files.ReadText("src/Fixture.Trading/OrderService.cs", lines: "1-5", cancellationToken: cancellationToken),
            files.FindFiles("**/*.cs", tracked: true, cancellationToken: cancellationToken),
            files.FindFiles(globs: ["*.json", "**/*.csproj"], cancellationToken: cancellationToken),
            files.FindFiles(globs: ["*.md", "**/OrderService.cs"], tracked: true, cancellationToken: cancellationToken));
    }
}
