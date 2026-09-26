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
    public void Remember_AGreenRunThatPassedTheRememberedFailure_LeavesNothingToRerun()
    {
        var lastRun = new LastTestRun();

        lastRun.Remember(Root, "first.csproj", ["Ns.Tests.One"]);
        lastRun.Remember(Root, "second.csproj", [], passedTests: ["Ns.Tests.One"]);

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

    [Fact]
    public void Remember_AGreenRunThatNeverRanTheRememberedFailure_KeepsIt()
    {
        var lastRun = new LastTestRun();

        lastRun.Remember(Root, "all.slnx", ["Ns.Tests.One"]);
        lastRun.Remember(Root, "all.slnx", []);

        Assert.Equal(["Ns.Tests.One"], lastRun.Memory.FailedTests);
        Assert.True(lastRun.Memory.Covers(Root));
    }

    [Fact]
    public void After_AGreenNarrowerRunThatNeverRanTheRememberedFailures_KeepsThemUnderTheirOwnTarget()
    {
        var red = new TestRunMemory(Root, "all.slnx", ["Ns.Tests.One", "Ns.Tests.Two"]);

        var after = red.After(new TestRunMemory(Root, "one.csproj", []), ["Ns.Tests.Three"], 200);

        Assert.Equal("all.slnx", after.Target);
        Assert.Equal(["Ns.Tests.One", "Ns.Tests.Two"], after.FailedTests);
        Assert.True(after.Covers(Root));
    }

    [Fact]
    public void After_AGreenRunThatPassedOneRememberedFailure_DropsOnlyThatOne()
    {
        var red = new TestRunMemory(Root, "all.slnx", ["Ns.Tests.One", "Ns.Tests.Two"]);

        var after = red.After(new TestRunMemory(Root, "all.slnx", []), ["Ns.Tests.One"], 200);

        Assert.Equal(["Ns.Tests.Two"], after.FailedTests);
    }

    [Fact]
    public void After_AGreenRunThatPassedEveryRememberedFailure_LeavesNothingToRerun()
    {
        var red = new TestRunMemory(Root, "all.slnx", ["Ns.Tests.One"]);

        var after = red.After(new TestRunMemory(Root, "one.csproj", []), ["Ns.Tests.One", "Ns.Tests.Other"], 200);

        Assert.Equal("one.csproj", after.Target);
        Assert.False(after.Covers(Root));
    }

    [Fact]
    public void After_ARedRunOfTheSameTargetAndScope_UnionsItsFailuresWithTheOutstandingOnes()
    {
        var red = new TestRunMemory(Root, "all.slnx", ["Ns.Tests.One", "Ns.Tests.Two"], new BuildScope("Release", null, ["A=1"]));
        var next = new TestRunMemory(Root, "all.slnx", ["Ns.Tests.Three"], new BuildScope("Release", null, ["A=1"]));

        var after = red.After(next, ["Ns.Tests.One"], 200);

        Assert.Equal(["Ns.Tests.Two", "Ns.Tests.Three"], after.FailedTests);
    }

    [Fact]
    public void After_ARedRunOfAnotherTarget_ReplacesTheList()
    {
        var red = new TestRunMemory(Root, "all.slnx", ["Ns.Tests.One"]);

        var after = red.After(new TestRunMemory(Root, "two.csproj", ["Ns.Tests.Two"]), [], 200);

        Assert.Equal("two.csproj", after.Target);
        Assert.Equal(["Ns.Tests.Two"], after.FailedTests);
    }

    [Fact]
    public void After_ARedRunUnderAnotherConfiguration_ReplacesTheList()
    {
        var red = new TestRunMemory(Root, "all.slnx", ["Ns.Tests.One"], new BuildScope("Debug", null));

        var after = red.After(new TestRunMemory(Root, "all.slnx", ["Ns.Tests.Two"], new BuildScope("Release", null)), [], 200);

        Assert.Equal(["Ns.Tests.Two"], after.FailedTests);
        Assert.Equal("Release", after.Scope.Configuration);
    }

    [Fact]
    public void After_ARunInAnotherWorkspace_ForgetsTheRememberedFailures()
    {
        var red = new TestRunMemory(Root, "all.slnx", ["Ns.Tests.One"]);

        var after = red.After(new TestRunMemory("C:\\other", "all.slnx", []), [], 200);

        Assert.Equal("C:\\other", after.WorkspaceRoot);
        Assert.False(after.Covers(Root));
    }

    [Fact]
    public void After_AUnionPastTheCap_StaysBounded()
    {
        var red = new TestRunMemory(Root, "big.slnx", [.. Enumerable.Range(0, 150).Select(index => "Ns.Tests.Old" + index.ToString(CultureInfo.InvariantCulture))]);
        var next = new TestRunMemory(Root, "big.slnx", [.. Enumerable.Range(0, 150).Select(index => "Ns.Tests.New" + index.ToString(CultureInfo.InvariantCulture))]);

        var after = red.After(next, [], 200);

        Assert.Equal(200, after.FailedTests.Length);
        Assert.Equal("Ns.Tests.Old0", after.FailedTests[0]);
    }

    [Fact]
    public void After_AGreenUnfilteredRunOfTheSameTargetThatNoLongerContainsTheFailure_ForgetsIt()
    {
        var red = new TestRunMemory(Root, "all.slnx", ["Ns.Tests.Deleted"]);

        var after = red.After(new TestRunMemory(Root, "all.slnx", [], Unfiltered: true), ["Ns.Tests.Other"], 200);

        Assert.False(after.Covers(Root));
    }

    [Fact]
    public void After_AGreenFilteredRunOfTheSameTargetThatNeverRanTheFailure_KeepsIt()
    {
        var red = new TestRunMemory(Root, "all.slnx", ["Ns.Tests.One"]);

        var after = red.After(new TestRunMemory(Root, "all.slnx", []), ["Ns.Tests.Other"], 200);

        Assert.Equal(["Ns.Tests.One"], after.FailedTests);
    }
}
