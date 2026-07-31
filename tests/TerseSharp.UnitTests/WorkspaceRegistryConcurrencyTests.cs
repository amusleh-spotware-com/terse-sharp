using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class WorkspaceRegistryConcurrencyTests
{
    [Fact]
    public async Task ResolveAndUnload_RunningConcurrently_AlwaysReturnAConsistentState()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        var readers = Enumerable.Range(0, 8).Select(_ => Task.Run(Read, TestContext.Current.CancellationToken));
        var writers = Enumerable.Range(0, 4).Select(_ => Task.Run(Churn, TestContext.Current.CancellationToken));

        await Task.WhenAll(readers.Concat(writers));

        void Read()
        {
            for (var index = 0; index < 200; index++)
            {
                var resolved = registry.Resolve(null, null);

                Assert.True(
                    resolved.IsOk || resolved.Error!.Code is TerseErrorCode.WorkspaceNotLoaded,
                    $"unexpected state: {resolved.Error?.Code}");
                Assert.InRange(registry.All().Count, 0, 1);

                if (resolved.IsOk)
                    resolved.Value!.Dispose();
            }
        }

        void Churn()
        {
            for (var index = 0; index < 200; index++)
                registry.Unload(Fixtures.SolutionPath);
        }
    }

    [Fact]
    public async Task Unload_CalledTwiceConcurrently_DisposesOnlyOnce()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ => Task.Run(
                () => registry.Unload(Fixtures.SolutionPath),
                TestContext.Current.CancellationToken)));

        Assert.Single(results, unloaded => unloaded);
    }
}
