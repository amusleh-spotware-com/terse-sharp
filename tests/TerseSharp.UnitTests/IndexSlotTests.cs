using System.Globalization;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class IndexSlotTests
{
    [Fact]
    public async Task Get_WhileASlowBuildHoldsTheGate_MakesTheWaiterAtANewerGenerationRebuild()
    {
        using var slot = new IndexSlot<string>();
        using var release = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);

        var slow = Task.Run(() => slot.Get(new IndexKey(3, Trusted: true), _ => Built(entered, release, "gen3")));

        entered.Wait(TestContext.Current.CancellationToken);

        var waiter = Task.Run(() => slot.Get(new IndexKey(4, Trusted: true), _ => "gen4"));

        release.Set();

        Assert.Equal("gen3", await slow);
        Assert.Equal("gen4", await waiter);
        Assert.Equal(2, slot.Misses);
        Assert.Equal(0, slot.Hits);
    }

    [Fact]
    public async Task Get_WhileASlowBuildHoldsTheGate_ServesTheWaiterAtTheSameGeneration()
    {
        using var slot = new IndexSlot<string>();
        using var release = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);

        var slow = Task.Run(() => slot.Get(new IndexKey(3, Trusted: true), _ => Built(entered, release, "gen3")));

        entered.Wait(TestContext.Current.CancellationToken);

        var waiter = Task.Run(() => slot.Get(new IndexKey(3, Trusted: true), _ => "rebuilt"));

        release.Set();

        Assert.Equal("gen3", await slow);
        Assert.Equal("gen3", await waiter);
        Assert.Equal(1, slot.Misses);
        Assert.Equal(1, slot.Hits);
    }

    [Fact]
    public void Get_WhenTheGenerationIsNotTrusted_SweepsAgainOnEveryCall()
    {
        using var slot = new IndexSlot<string>();
        var builds = 0;

        var first = slot.Get(new IndexKey(0, Trusted: false), _ => Counted(ref builds));
        var second = slot.Get(new IndexKey(0, Trusted: false), _ => Counted(ref builds));

        Assert.NotEqual(first, second);
        Assert.Equal(2, builds);
        Assert.Equal(0, slot.Hits);
    }

    [Fact]
    public void Get_AfterTheGenerationResetsToZero_RebuildsRatherThanServingTheHigherOne()
    {
        using var slot = new IndexSlot<string>();

        Assert.Equal("high", slot.Get(new IndexKey(7, Trusted: true), _ => "high"));
        Assert.Equal("reset", slot.Get(new IndexKey(0, Trusted: true), _ => "reset"));
        Assert.Equal(2, slot.Misses);
        Assert.Equal(0, slot.Hits);
    }

    private static string Counted(ref int builds) => (++builds).ToString(CultureInfo.InvariantCulture);

    private static string Built(ManualResetEventSlim entered, ManualResetEventSlim release, string value)
    {
        entered.Set();
        release.Wait(TestContext.Current.CancellationToken);

        return value;
    }
}
