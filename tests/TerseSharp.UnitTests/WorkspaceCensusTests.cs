using System.Runtime.CompilerServices;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class WorkspaceCensusTests
{
    [Fact]
    public void Retained_WithNothingRetired_AnswersZeroWithoutCollecting()
    {
        var collections = 0;

        Assert.Equal(0, new RetiredSet().Retained(() => collections++));
        Assert.Equal(0, collections);
    }

    [Fact]
    public void Retained_ARetiredObjectStillReferenced_IsCountedOnceAfterOneCollection()
    {
        var set = new RetiredSet();
        var held = new object();
        var collections = 0;

        set.Add(held);
        set.Add(held);

        Assert.Equal(1, set.Retained(() => collections++));
        Assert.Equal(1, collections);
        GC.KeepAlive(held);
    }

    [Fact]
    public void Retained_ARetiredObjectNothingReferences_IsGoneAfterACollection()
    {
        var set = new RetiredSet();

        AddUnreferenced(set);

        Assert.Equal(0, set.Retained(() =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }));
    }

    [Theory]
    [InlineData(0, "retired=0 alive")]
    [InlineData(2, "retired=2 alive after a compacting collection")]
    public void Detail_NamesTheRetainedCount(int retained, string expected) => Assert.Equal(expected, WorkspaceCensus.Detail(retained));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddUnreferenced(RetiredSet set) => set.Add(new object());
}
