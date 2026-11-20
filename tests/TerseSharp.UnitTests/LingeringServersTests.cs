using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class LingeringServersTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Describe_ServersStartedOverADayAgo_AreNamedOldestFirstWithTheirAge()
    {
        var warning = LingeringServers.Describe([new(41, Now.AddHours(-30)), new(42, Now.AddHours(-2)), new(43, Now.AddDays(-5))], Now);

        Assert.StartsWith("WARNING 2 other terse server(s) running for over a day: pid=43 age=5d, pid=41 age=30h - ", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_OnlyRecentServers_SaysNothing() => Assert.Null(LingeringServers.Describe([new(42, Now.AddHours(-2))], Now));
}
