using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ReplayRememberTests
{
    [Fact]
    public void Rememberable_ForAPartialOrFailedAnswer_IsFalseSoAnIdenticalRetryRunsAgain()
    {
        Assert.False(ReplayGate.Rememberable("0 findings\nPARTIAL 0 of 2 project(s) analyzed by the 600 s deadline"));
        Assert.False(ReplayGate.Rememberable("ERROR InvalidArgument: x"));
        Assert.True(ReplayGate.Rememberable("0 findings\nengines=compiler"));
    }
}
