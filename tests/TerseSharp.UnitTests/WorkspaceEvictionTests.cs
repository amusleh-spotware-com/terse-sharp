using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class WorkspaceEvictionTests
{
    [Fact]
    public async Task LoadAsync_OfAPathTheLimitEvicted_NamesWhenAndByWhichLoadItWasEvicted()
    {
        using var registry = new WorkspaceRegistry(maxWorkspaces: 1, watch: false);

        var first = await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        await registry.LoadAsync(Fixtures.RazorSolutionPath, TestContext.Current.CancellationToken);
        var evicted = await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        var resident = await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        Assert.Null(first.Eviction);
        Assert.Equal(Path.GetFullPath(Fixtures.RazorSolutionPath), evicted.Eviction?.EvictedBy);
        Assert.Equal(1, evicted.Eviction?.Capacity);
        Assert.Null(resident.Eviction);
        Assert.Null(registry.All()[0].Load.Eviction);
    }

    [Fact]
    public void Remember_PastSixteenEvictions_ForgetsTheOldestAndKeepsTheNewest()
    {
        var memory = new EvictionMemory();
        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

        for (var index = 0; index < 17; index++)
            memory.Remember(Name(index), "By.slnx", 4, start.AddSeconds(index));

        Assert.Null(memory.Recalled(Name(0)));
        Assert.Equal(start.AddSeconds(1), memory.Recalled(Name(1))?.EvictedUtc);
        Assert.Equal(start.AddSeconds(16), memory.Recalled(Name(16))?.EvictedUtc);
    }

    [Fact]
    public void Describe_AnEvictionNinetySecondsOld_NamesTheAgeTheEvictingLoadAndTheLimit()
    {
        var eviction = new WorkspaceEviction(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero), "C:\\repo\\Other.slnx", 4);

        Assert.Equal(
            "evicted 1.5m ago by the load of C:\\repo\\Other.slnx - at most 4 workspace(s) stay loaded (--max-workspaces or TERSE_MAX_WORKSPACES raises it), so this load paid in full again",
            eviction.Describe(new DateTimeOffset(2026, 11, 20, 10, 1, 30, TimeSpan.Zero)));
    }

    private static string Name(int index) => "C:\\repo\\S" + (char)('a' + index) + ".slnx";
}
