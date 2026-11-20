using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class GateTimingTests
{
    private const string Applied = "edit_text applied  src/A.cs  changedLines=1  errors=0 warnings=0";

    [Fact]
    public void Appended_OverTheThreshold_AddsOneLineNamingEveryPhaseTheScopeAndTheProjectCount()
    {
        var analysis = new AnalysisTiming(TimeSpan.FromMilliseconds(40.9), TimeSpan.FromMilliseconds(30000), TimeSpan.FromMilliseconds(25000), TimeSpan.FromMilliseconds(300), new GateScope(false, 3));
        var timing = new GateTiming(TimeSpan.FromMilliseconds(58500), TimeSpan.FromMilliseconds(120), analysis, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(2000));

        Assert.Equal(Applied + "\ntiming totalMs=58500 diffMs=120 shapeMs=40 realizeMs=30000 compileMs=25000 hintsMs=300 policyMs=10 applyMs=2000 gate=dependents projects=3", timing.Appended(Applied, TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData(true, 1, " gate=project projects=1")]
    [InlineData(false, 0, " gate=none projects=0")]
    [InlineData(true, 0, " gate=none projects=0")]
    public void Appended_ForANarrowedOrAnUnanalysedGate_NamesItsScope(bool narrowed, int projects, string expected) =>
        Assert.EndsWith(expected, new GateTiming(TimeSpan.FromSeconds(11), TimeSpan.Zero, new AnalysisTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, new GateScope(narrowed, projects)), TimeSpan.Zero, TimeSpan.Zero).Appended(Applied, TimeSpan.FromSeconds(10)), StringComparison.Ordinal);

    [Fact]
    public void Appended_AtExactlyTheThreshold_ReturnsTheResponseUntouched() =>
        Assert.Same(Applied, new GateTiming(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(4), default, TimeSpan.Zero, TimeSpan.FromSeconds(6)).Appended(Applied, TimeSpan.FromSeconds(10)));

    [Fact]
    public void Appended_ForTheDefaultTiming_NeverAddsALineEvenAtAZeroThreshold() =>
        Assert.Same(Applied, default(GateTiming).Appended(Applied, TimeSpan.Zero));

    [Theory]
    [InlineData(null, 10000)]
    [InlineData("", 10000)]
    [InlineData("0", 0)]
    [InlineData("2500", 2500)]
    [InlineData("-1", 10000)]
    [InlineData("ten", 10000)]
    [InlineData("99999999999", 10000)]
    public void Configured_FromTheEnvironmentValue_ParsesWholeMillisecondsOrFallsBackToTenSeconds(string? value, int expectedMilliseconds) =>
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), GateTiming.Configured(value));
}
