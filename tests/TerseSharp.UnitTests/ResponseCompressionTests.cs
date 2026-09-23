using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ResponseCompressionTests
{
    [Fact]
    public void Counters_ForAnEditThatAddedNothingToAlreadyBrokenCode_LeadsWithTheDeltaAndCallsTheRestInScope() =>
        Assert.Equal("errors=+0 (58828 in scope) warnings=+0 (1063 in scope)", ResponseCompression.Counters(58828, 0, 1063, 0));

    [Fact]
    public void Counters_ForAnEditThatIntroducedWarnings_LeadsWithTheDeltaItCaused() =>
        Assert.Equal("warnings=+2 (7 in scope)", ResponseCompression.Counters(0, 0, 7, 2));

    [Fact]
    public void Counters_ForACleanEdit_SaysNothing() =>
        Assert.Equal(string.Empty, ResponseCompression.Counters(0, 0, 0, 0));
}
