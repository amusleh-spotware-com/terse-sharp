using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class DiagnosticHistoryTests : IDisposable
{
    private readonly string scope = "analyze:" + Guid.NewGuid().ToString("N");

    public void Dispose() => DiagnosticHistory.Forget();

    [Fact]
    public void Record_OnTheFirstRun_TreatsEverythingAsNew()
    {
        var delta = DiagnosticHistory.Record(scope, ["CS0001 a", "CS0002 b"]);

        Assert.Equal(2, delta.Appeared.Count);
        Assert.Empty(delta.Fixed);
        Assert.Equal(0, delta.Unchanged);
    }

    [Fact]
    public void Record_OnASecondIdenticalRun_ReportsNothingNew()
    {
        DiagnosticHistory.Record(scope, ["CS0001 a", "CS0002 b"]);

        var delta = DiagnosticHistory.Record(scope, ["CS0001 a", "CS0002 b"]);

        Assert.Empty(delta.Appeared);
        Assert.Empty(delta.Fixed);
        Assert.Equal(2, delta.Unchanged);
    }

    [Fact]
    public void Record_SeparatesWhatAppearedFromWhatWasFixed()
    {
        DiagnosticHistory.Record(scope, ["CS0001 a", "CS0002 b"]);

        var delta = DiagnosticHistory.Record(scope, ["CS0002 b", "CS0003 c"]);

        Assert.Equal(["CS0003 c"], delta.Appeared);
        Assert.Equal(["CS0001 a"], delta.Fixed);
        Assert.Equal(1, delta.Unchanged);
    }

    [Fact]
    public void Record_KeepsScopesApart()
    {
        DiagnosticHistory.Record(scope, ["CS0001 a"]);

        var other = DiagnosticHistory.Record(scope + "-other", ["CS0009 z"]);

        Assert.Equal(["CS0009 z"], other.Appeared);
    }

    [Fact]
    public void Knows_IsFalseUntilTheScopeHasBeenRecorded()
    {
        Assert.False(DiagnosticHistory.Knows(scope));

        DiagnosticHistory.Record(scope, []);

        Assert.True(DiagnosticHistory.Knows(scope));
    }
}
