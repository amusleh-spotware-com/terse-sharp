using TerseSharp.Core;
using Xunit;

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
        Assert.Equal(Path.GetFullPath(Fixtures.SolutionPath), result.Value!.SolutionPath);
    }
}
