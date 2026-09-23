using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class RealizedNoteTests
{
    [Fact]
    public void Realized_OnTheFirstRealizationOfALoad_SaysItIsPaidOncePerLoad() =>
        Assert.Equal(
            "compilations=realized in 7414ms (once per load, not per call)",
            ToolContext.Realized(7414, drops: 0, TimeSpan.Zero));

    [Fact]
    public void Realized_AfterAnIdleDrop_SaysItIsARealizationAgainAndWhy() =>
        Assert.Equal(
            "compilations=realized in 84782ms (again - drop #3 released them after 2m idle; --idle-minutes or TERSE_IDLE_MINUTES=0 keeps them)",
            ToolContext.Realized(84782, drops: 3, TimeSpan.FromMinutes(2)));
}
