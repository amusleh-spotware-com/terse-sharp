using System.Globalization;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class LastTestRunTests
{
    private const string Root = "C:\\repo";

    [Fact]
    public void Memory_BeforeAnyRun_CoversNothing()
    {
        var lastRun = new LastTestRun();

        Assert.False(lastRun.Memory.Covers(Root));
    }

    [Fact]
    public void Remember_AFailedRun_KeepsTheWorkspaceTargetAndNames()
    {
        var lastRun = new LastTestRun();

        lastRun.Remember(Root, "Fixture.Trading.Tests.csproj", ["Ns.Tests.One", "Ns.Tests.Two"]);

        Assert.Equal(Root, lastRun.Memory.WorkspaceRoot);
        Assert.Equal("Fixture.Trading.Tests.csproj", lastRun.Memory.Target);
        Assert.Equal(["Ns.Tests.One", "Ns.Tests.Two"], lastRun.Memory.FailedTests);
        Assert.True(lastRun.Memory.Covers(Root));
    }

    [Fact]
    public void Covers_AnotherWorkspace_IsFalse()
    {
        var lastRun = new LastTestRun();

        lastRun.Remember(Root, "one.csproj", ["Ns.Tests.One"]);

        Assert.False(lastRun.Memory.Covers("C:\\other"));
    }

    [Fact]
    public void Covers_AWorkspaceWhoseNameExtendsTheRoot_IsFalse()
    {
        var lastRun = new LastTestRun();

        lastRun.Remember(Root, "one.csproj", ["Ns.Tests.One"]);

        Assert.False(lastRun.Memory.Covers("C:\\repository"));
    }

    [Fact]
    public void Remember_AGreenRun_LeavesNothingToRerun()
    {
        var lastRun = new LastTestRun();

        lastRun.Remember(Root, "first.csproj", ["Ns.Tests.One"]);
        lastRun.Remember(Root, "second.csproj", []);

        Assert.Equal("second.csproj", lastRun.Memory.Target);
        Assert.False(lastRun.Memory.Covers(Root));
    }

    [Fact]
    public void Remember_MoreFailuresThanTheCap_KeepsTheFilterBounded()
    {
        var lastRun = new LastTestRun();

        lastRun.Remember(Root, "big.csproj", Enumerable.Range(0, 500).Select(index => "Ns.Tests.Case" + index.ToString(CultureInfo.InvariantCulture)));

        Assert.Equal(200, lastRun.Memory.FailedTests.Length);
    }
}
