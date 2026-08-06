using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class IdleLimitTests
{
    [Fact]
    public void Resolve_WithNothingSet_UsesTheFifteenMinuteDefault() =>
        Assert.Equal(TimeSpan.FromMinutes(15), IdleLimit.Resolve(null, null));

    [Fact]
    public void Resolve_WithZero_TurnsTheSweepOff() =>
        Assert.Equal(TimeSpan.Zero, IdleLimit.Resolve(0, null));

    [Fact]
    public void Resolve_WithZeroInTheEnvironment_TurnsTheSweepOff() =>
        Assert.Equal(TimeSpan.Zero, IdleLimit.Resolve(null, "0"));

    [Fact]
    public void Resolve_PrefersTheOptionOverTheEnvironment() =>
        Assert.Equal(TimeSpan.FromMinutes(3), IdleLimit.Resolve(3, "60"));

    [Fact]
    public void Resolve_WithAnUnparsableEnvironment_FallsBackToTheDefault() =>
        Assert.Equal(TimeSpan.FromMinutes(15), IdleLimit.Resolve(null, "soon"));

    [Fact]
    public void Resolve_WithANegativeEnvironment_FallsBackToTheDefault() =>
        Assert.Equal(TimeSpan.FromMinutes(15), IdleLimit.Resolve(null, "-5"));
}
