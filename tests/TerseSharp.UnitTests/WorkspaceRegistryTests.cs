using TerseSharp.Core;

namespace TerseSharp.UnitTests;

[Collection(nameof(FixtureSolutionCollection))]
public sealed class WorkspaceRegistryTests
{
    [Fact]
    public void Resolve_WithNothingLoaded_ReturnsWorkspaceNotLoaded()
    {
        using var registry = new WorkspaceRegistry();

        var result = registry.Resolve(null, null);

        Assert.False(result.IsOk);
        Assert.Equal(TerseErrorCode.WorkspaceNotLoaded, result.Error!.Code);
    }

    [Fact]
    public async Task LoadAsync_TwiceForOnePath_LoadsOnce()
    {
        using var registry = new WorkspaceRegistry();

        var first = await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        var second = await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Single(registry.All());
    }

    [Fact]
    public async Task LoadAsync_FixtureSolution_LoadsProjectsWithoutFailures()
    {
        using var registry = new WorkspaceRegistry();

        var result = await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ProjectCount);
        Assert.Empty(result.Failures);
        Assert.Contains(
            registry.All()[0].Solution.Projects.Single().Documents,
            document => document.Name is "OrderService.cs");
    }

    [Fact]
    public async Task Unload_AfterLoad_RemovesTheWorkspace()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        Assert.True(registry.Unload(Fixtures.SolutionPath));
        Assert.Empty(registry.All());
    }

    [Fact]
    public async Task Resolve_WithOneWorkspaceLoaded_ReturnsIt()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        var result = registry.Resolve(null, null);

        Assert.True(result.IsOk);
        Assert.Equal(Path.GetFullPath(Fixtures.SolutionPath), result.Value!.Workspace.SolutionPath);

        result.Value!.Dispose();
    }

    [Fact]
    public async Task Unload_WhileALeaseIsHeld_LeavesTheWorkspaceUsable()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        var resolved = registry.Resolve(null, null);

        Assert.True(registry.Unload(Fixtures.SolutionPath));
        Assert.NotEmpty(resolved.Value!.Workspace.Solution.Projects);

        resolved.Value!.Dispose();
    }

    [Fact]
    public async Task Unload_AfterTheLastLeaseIsReleased_DisposesTheWorkspace()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        var resolved = registry.Resolve(null, null);
        var workspace = resolved.Value!.Workspace;

        Assert.True(registry.Unload(Fixtures.SolutionPath));

        resolved.Value!.Dispose();

        Assert.Empty(workspace.Solution.Projects);
    }

    [Fact]
    public async Task LoadAsync_BeyondTheLimit_UnloadsTheLeastRecentlyUsed()
    {
        using var registry = new WorkspaceRegistry(maxWorkspaces: 1);

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        await registry.LoadAsync(Fixtures.RazorSolutionPath, TestContext.Current.CancellationToken);

        var loaded = registry.All();

        Assert.Single(loaded);
        Assert.Equal(Path.GetFullPath(Fixtures.RazorSolutionPath), loaded[0].SolutionPath);
    }

    [Fact]
    public async Task LoadAsync_WithinTheLimit_KeepsBothWorkspaces()
    {
        using var registry = new WorkspaceRegistry(maxWorkspaces: 2);

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        await registry.LoadAsync(Fixtures.RazorSolutionPath, TestContext.Current.CancellationToken);

        Assert.Equal(2, registry.All().Count);
    }

    [Fact]
    public async Task Unload_AfterLoad_ForgetsTheRazorGeneratedCacheForItsProjects()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.RazorSolutionPath, TestContext.Current.CancellationToken);

        var loaded = registry.All()[0];
        var projects = loaded.Solution.ProjectIds;

        await RazorGeneratedMap.InProjectAsync(loaded.Solution.Projects.First(), TestContext.Current.CancellationToken);

        Assert.True(RazorGeneratedMap.Knows(projects[0]));
        Assert.True(registry.Unload(Fixtures.RazorSolutionPath));
        Assert.False(RazorGeneratedMap.Knows(projects[0]));
    }

    [Fact]
    public async Task DropIdleCompilations_WithTheSweepOff_DropsNothing()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        Assert.Equal(0, registry.DropIdleCompilations(TimeSpan.Zero));
        Assert.False(registry.All()[0].CompilationsDropped);
    }

    [Fact]
    public async Task DropIdleCompilations_WithAWorkspaceYoungerThanTheWindow_DropsNothing()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        Assert.Equal(0, registry.DropIdleCompilations(TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task DropIdleCompilations_OnAnIdleWorkspace_ReForksTheSolutionAndSaysSo()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        var workspace = registry.All()[0];
        var before = workspace.Solution;

        Assert.Equal(1, registry.DropIdleCompilations(TimeSpan.Zero.Add(TimeSpan.FromTicks(1))));
        Assert.True(workspace.CompilationsDropped);
        Assert.NotSame(before, workspace.Solution);
    }

    [Fact]
    public async Task DropIdleCompilations_LeavesTheWorkspaceUsable()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        registry.DropIdleCompilations(TimeSpan.FromTicks(1));

        var documents = registry.All()[0].Solution.Projects.Single().Documents;

        Assert.NotEmpty(documents);
        Assert.DoesNotContain(documents, document => document.FilePath is null);
    }

    [Fact]
    public async Task DropIdleCompilations_IsNotRepeatedUntilTheWorkspaceIsUsedAgain()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        Assert.Equal(1, registry.DropIdleCompilations(TimeSpan.FromTicks(1)));
        Assert.Equal(0, registry.DropIdleCompilations(TimeSpan.FromTicks(1)));

        registry.All()[0].Touch();

        Assert.Equal(1, registry.DropIdleCompilations(TimeSpan.FromTicks(1)));
    }

    [Fact]
    public async Task DropIdleCompilations_UnderMemoryPressure_DropsOnlyOnceTheMinimumIdleHasPassed()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        Assert.Equal(0, registry.DropIdleCompilations(TimeSpan.FromHours(1), long.MaxValue));
    }

    [Fact]
    public async Task DropIdleCompilations_WhileALeaseIsOutstanding_DropsNothing()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;

        Assert.Equal(0, registry.DropIdleCompilations(TimeSpan.FromTicks(1)));
        Assert.False(registry.All()[0].CompilationsDropped);
    }

    [Fact]
    public async Task TakeDroppedNotice_ReportsTheDropExactlyOnce()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        registry.DropIdleCompilations(TimeSpan.FromTicks(1));

        var workspace = registry.All()[0];

        Assert.True(workspace.TakeDroppedNotice());
        Assert.False(workspace.TakeDroppedNotice());
    }

    [Fact]
    public async Task TakeDroppedNotice_SurvivesTheTouchThatResolvingTheWorkspacePerforms()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        registry.DropIdleCompilations(TimeSpan.FromTicks(1));

        using var lease = registry.Resolve(null, null).Value!;

        Assert.True(lease.Workspace.TakeDroppedNotice());
    }

    [Fact]
    public async Task LoadAsync_WithATargetFramework_RecordsItOnTheLoadResult()
    {
        using var registry = new WorkspaceRegistry();

        var result = await registry.LoadAsync(Fixtures.SolutionPath, "net10.0", TestContext.Current.CancellationToken);

        Assert.Equal("net10.0", result.TargetFramework);
    }

    [Fact]
    public async Task ReloadAsync_KeepsTheTargetFrameworkTheWorkspaceWasLoadedUnder()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, "net10.0", TestContext.Current.CancellationToken);

        var reloaded = await registry.ReloadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        Assert.Equal("net10.0", reloaded.TargetFramework);
        Assert.Equal("net10.0", registry.All()[0].Load.TargetFramework);
    }

    [Fact]
    public async Task LoadAsync_WithADifferentTargetFramework_ReplacesTheWorkspaceInsteadOfLeakingIt()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, "net10.0", TestContext.Current.CancellationToken);

        var first = registry.All()[0];
        var second = await registry.LoadAsync(Fixtures.SolutionPath, null, TestContext.Current.CancellationToken);

        Assert.Single(registry.All());
        Assert.Null(second.TargetFramework);
        Assert.NotSame(first, registry.All()[0]);
        Assert.False(first.DropCompilations());
    }

    [Fact]
    public async Task LoadAsync_WithTheSameTargetFramework_ReusesTheLoadedWorkspace()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, "net10.0", TestContext.Current.CancellationToken);

        var first = registry.All()[0];

        await registry.LoadAsync(Fixtures.SolutionPath, "net10.0", TestContext.Current.CancellationToken);

        Assert.Same(first, registry.All()[0]);
    }

    [Fact]
    public async Task TakeDroppedNotice_IsNotCarriedPastTheCallThatFollowsTheDrop()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        registry.DropIdleCompilations(TimeSpan.FromTicks(1));

        using (var first = registry.Resolve(null, null).Value!)
            Assert.True(first.Workspace.TakeDroppedNotice());

        using var second = registry.Resolve(null, null).Value!;

        Assert.False(second.Workspace.TakeDroppedNotice());
    }

    [Fact]
    public async Task TakeDroppedNotice_IsNotConsumedByAResolveThatCannotReRealizeCompilations()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        registry.DropIdleCompilations(TimeSpan.FromTicks(1));

        using (var reader = registry.Resolve(null, null, semantic: false).Value!)
            Assert.True(reader.Workspace.CompilationsDropped);

        using var semantic = registry.Resolve(null, null, semantic: true).Value!;

        Assert.True(semantic.Workspace.TakeDroppedNotice());
    }
}
