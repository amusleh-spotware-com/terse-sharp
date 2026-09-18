using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ActiveRunsTests
{
    [Fact]
    public void TryEnter_WhileARunIsStillInFlightForTheSameSolution_RefusesAndNamesTheRunThatHoldsIt()
    {
        var solution = Path.Combine(Path.GetTempPath(), "terse-active-one.slnx");

        Assert.True(ActiveRuns.TryEnter(solution, "run_tests", "project=\"Selection.Core.Tests\"", out _));
        try
        {
            Assert.False(ActiveRuns.TryEnter(solution, "build", string.Empty, out var holder));
            Assert.Equal("run_tests", holder.Tool);

            var rendered = Errors.RunInFlight("build", holder).Render();

            Assert.Contains("RunInFlight", rendered, StringComparison.Ordinal);
            Assert.Contains("run_tests is already running", rendered, StringComparison.Ordinal);
            Assert.Contains("project=\"Selection.Core.Tests\"", rendered, StringComparison.Ordinal);
            Assert.Contains("remedy:", rendered, StringComparison.Ordinal);
        }
        finally
        {
            ActiveRuns.Leave(solution);
        }

        Assert.True(ActiveRuns.TryEnter(solution, "build", string.Empty, out _));
        ActiveRuns.Leave(solution);
    }

    [Fact]
    public void TryEnter_ForAnotherSolution_IsUnaffectedByTheRunInFlightOverTheFirst()
    {
        var first = Path.Combine(Path.GetTempPath(), "terse-active-first.slnx");
        var second = Path.Combine(Path.GetTempPath(), "terse-active-second.slnx");

        Assert.True(ActiveRuns.TryEnter(first, "run_tests", string.Empty, out _));
        try
        {
            Assert.True(ActiveRuns.TryEnter(second, "run_tests", string.Empty, out _));
            ActiveRuns.Leave(second);
        }
        finally
        {
            ActiveRuns.Leave(first);
        }
    }
}
