using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ActiveRunsTests
{
    [Fact]
    public void TryEnter_WhileARunIsStillInFlightForTheSameSolution_RefusesAndNamesTheRunThatHoldsIt()
    {
        var solution = Path.Combine(Path.GetTempPath(), "terse-active-one.slnx");
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();

        Assert.True(ActiveRuns.TryEnter(solution, "run_tests", "project=\"Selection.Core.Tests\"", out _, first));
        try
        {
            Assert.False(ActiveRuns.TryEnter(solution, "build", string.Empty, out var holder, second));
            Assert.Equal("run_tests", holder.Tool);

            var rendered = Errors.RunInFlight("build", holder).Render();

            Assert.Contains("RunInFlight", rendered, StringComparison.Ordinal);
            Assert.Contains("run_tests is already running", rendered, StringComparison.Ordinal);
            Assert.Contains("project=\"Selection.Core.Tests\"", rendered, StringComparison.Ordinal);
            Assert.Contains("remedy:", rendered, StringComparison.Ordinal);
            Assert.Contains("run_tests status=\"cancel\"", rendered, StringComparison.Ordinal);
            Assert.Contains("run_tests status=\"<id>\"", rendered, StringComparison.Ordinal);
        }
        finally
        {
            ActiveRuns.Leave(solution);
        }

        Assert.True(ActiveRuns.TryEnter(solution, "build", string.Empty, out _, second));
        ActiveRuns.Leave(solution);
    }

    [Fact]
    public void TryEnter_ForAnotherSolution_IsUnaffectedByTheRunInFlightOverTheFirst()
    {
        var first = Path.Combine(Path.GetTempPath(), "terse-active-first.slnx");
        var second = Path.Combine(Path.GetTempPath(), "terse-active-second.slnx");
        using var stop = new CancellationTokenSource();

        Assert.True(ActiveRuns.TryEnter(first, "run_tests", string.Empty, out _, stop));
        try
        {
            Assert.True(ActiveRuns.TryEnter(second, "run_tests", string.Empty, out _, stop));
            ActiveRuns.Leave(second);
        }
        finally
        {
            ActiveRuns.Leave(first);
        }
    }

    [Fact]
    public async Task CancelAsync_ForARunInFlight_CancelsItsStopAndAnswersOnceTheRunLeaves()
    {
        var solution = Path.Combine(Path.GetTempPath(), "terse-active-cancel.slnx");
        using var stop = new CancellationTokenSource();

        Assert.True(ActiveRuns.TryEnter(solution, "run_tests", string.Empty, out _, stop));
        using var leaving = stop.Token.Register(() => ActiveRuns.Leave(solution));

        var answer = await ActiveRuns.CancelAsync(solution, TestContext.Current.CancellationToken);

        Assert.True(stop.IsCancellationRequested);
        Assert.StartsWith("CANCELLED run_tests after ", answer, StringComparison.Ordinal);
        Assert.EndsWith("- the solution is free", answer, StringComparison.Ordinal);
        Assert.Null(ActiveRuns.Holding(solution));
    }

    [Fact]
    public async Task CancelAsync_WithNothingInFlight_AnswersThatThereIsNothingToCancel()
    {
        var answer = await ActiveRuns.CancelAsync(Path.Combine(Path.GetTempPath(), "terse-active-idle.slnx"), TestContext.Current.CancellationToken);

        Assert.Contains("nothing to cancel", answer, StringComparison.Ordinal);
        Assert.Contains("remedy:", answer, StringComparison.Ordinal);
    }
}
