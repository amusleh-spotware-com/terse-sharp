using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class TestRunRequestTests
{
    [Fact]
    public void Degree_ForASingleTarget_IsOneWhateverParallelAsksFor() =>
        Assert.Equal(1, Request(64, "one.csproj").Degree);

    [Fact]
    public void Degree_ForABatchWithNoParallelAsked_IsOneProcessPerCoreBoundedByTheBatch() =>
        Assert.Equal(Math.Min(Environment.ProcessorCount, 3), Request(0, "a.csproj", "b.csproj", "c.csproj").Degree);

    [Fact]
    public void Degree_ForABatchAskedForMoreProcessesThanProjects_NeverExceedsTheProjectCount() =>
        Assert.Equal(2, Request(64, "a.csproj", "b.csproj").Degree);

    [Fact]
    public void Degree_ForABatchAskedToRunOneAtATime_IsOne() =>
        Assert.Equal(1, Request(1, "a.csproj", "b.csproj").Degree);

    private static TestRunRequest Request(int parallel, params string[] targets) => new(
        targets[0],
        null,
        false,
        false,
        0,
        TimeSpan.FromMinutes(1),
        Targets: targets.Length is 1 ? default : [.. targets],
        Parallel: parallel);

    [Fact]
    public void IsSerial_ForASingleTarget_IsTrueWhateverParallelAsksFor() =>
        Assert.True(Request(64, "one.csproj").IsSerial);

    [Fact]
    public void IsSerial_ForABatchAskedToRunOneAtATime_IsTrue() =>
        Assert.True(Request(1, "a.csproj", "b.csproj").IsSerial);

    [Fact]
    public void IsSerial_ForABatchWithNoParallelAsked_IsFalseEvenWhereTheHostResolvesTheDegreeToOne() =>
        Assert.False(Request(0, "a.csproj", "b.csproj").IsSerial);
}
