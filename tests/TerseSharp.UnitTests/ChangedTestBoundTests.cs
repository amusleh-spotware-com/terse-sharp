using System.Globalization;
using TerseSharp.Core;
using TerseSharp.Server.Tools;

namespace TerseSharp.UnitTests;

public sealed class ChangedTestBoundTests
{
    [Fact]
    public void Crowded_ForASelectionWithinTheBound_KeepsTheSelectiveRun() =>
        Assert.False(BuildTools.Crowded(Selecting(BuildTools.MaxBatchedProjects)));

    [Fact]
    public void Crowded_ForASelectionPastTheBound_FallsBackToOneWholeSolutionRun() =>
        Assert.True(BuildTools.Crowded(Selecting(BuildTools.MaxBatchedProjects + 1)));

    [Fact]
    public void Crowded_ForAFullRun_IsNeverTrueBecauseThatIsAlreadyOneInvocation() =>
        Assert.False(BuildTools.Crowded(TestSelection.Full("nothing changed")));

    [Fact]
    public void CrowdedNote_NamesHowManyProjectsWereReachedAndTheBoundItPassed()
    {
        var note = BuildTools.CrowdedNote(Selecting(BuildTools.MaxBatchedProjects + 3));

        Assert.Contains("reaches 13 test projects", note, StringComparison.Ordinal);
        Assert.Contains("more than the 10", note, StringComparison.Ordinal);
        Assert.Contains("every test project of the solution was run instead", note, StringComparison.Ordinal);
        Assert.Contains("the timeout applies per project", note, StringComparison.Ordinal);
    }

    private static TestSelection Selecting(int projects) => new(
        [.. Enumerable.Range(0, projects).Select(index => string.Create(CultureInfo.InvariantCulture, $"tests/P{index}/P{index}.csproj"))],
        [],
        null);
}
