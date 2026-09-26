using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class CallTimingTests
{
    [Fact]
    public void ToString_TruncatesBothPhasesToWholeMilliseconds() =>
        Assert.Equal(
            "timing loadMs=1234 callMs=56",
            new CallTiming(TimeSpan.FromMilliseconds(1234.9), TimeSpan.FromMilliseconds(56.2)).ToString());

    [Fact]
    public void ToString_ForAnUnloadedWorkspace_ReportsZeroLoad() =>
        Assert.Equal("timing loadMs=0 callMs=7", new CallTiming(TimeSpan.Zero, TimeSpan.FromMilliseconds(7)).ToString());
}
