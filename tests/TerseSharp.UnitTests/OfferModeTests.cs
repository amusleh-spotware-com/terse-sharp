using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class OfferModeTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("once", true)]
    [InlineData("always", false)]
    [InlineData("ALWAYS", false)]
    public void Once_IsTheDefaultAndOnlyAlwaysTurnsItOff(string? environment, bool once) =>
        Assert.Equal(once, OfferMode.Once(environment));
}
