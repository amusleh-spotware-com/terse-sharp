using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ProjectFileGateTests
{
    [Fact]
    public async Task EnterAsync_ForThePathAlreadyHeld_WaitsUntilTheFirstHolderReleases()
    {
        var path = Path.Combine(Path.GetTempPath(), "terse-gate-probe.csproj");

        var first = await ProjectFileGate.EnterAsync([path], TestContext.Current.CancellationToken);
        var second = ProjectFileGate.EnterAsync([path], TestContext.Current.CancellationToken).AsTask();

        Assert.False(second.IsCompleted);

        await first.DisposeAsync();

        var held = await second.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await held.DisposeAsync();
    }

    [Fact]
    public async Task EnterAsync_ForAnotherPath_DoesNotWaitOnTheOneAlreadyHeld()
    {
        var held = Path.Combine(Path.GetTempPath(), "terse-gate-first.csproj");
        var other = Path.Combine(Path.GetTempPath(), "terse-gate-second.csproj");

        var first = await ProjectFileGate.EnterAsync([held], TestContext.Current.CancellationToken);
        var second = ProjectFileGate.EnterAsync([other], TestContext.Current.CancellationToken).AsTask();

        Assert.True(second.IsCompleted);

        await (await second).DisposeAsync();
        await first.DisposeAsync();
    }
}
