using TerseSharp.Core;

namespace TerseSharp.UnitTests;

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
}
