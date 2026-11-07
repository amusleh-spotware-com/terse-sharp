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

    [Fact]
    public async Task CancelAsync_ForARunWhoseStopIsAlreadyDisposed_SaysItFinishedInsteadOfClaimingACancel()
    {
        var solution = Path.Combine(Path.GetTempPath(), "terse-active-finished.slnx");
        var stop = new CancellationTokenSource();

        Assert.True(ActiveRuns.TryEnter(solution, "run_tests", string.Empty, out _, stop));
        stop.Dispose();

        try
        {
            var answer = await ActiveRuns.CancelAsync(solution, TestContext.Current.CancellationToken);

            Assert.DoesNotContain("CANCELLED", answer, StringComparison.Ordinal);
            Assert.Equal("run_tests finished before the cancel arrived - nothing was stopped", answer);
        }
        finally
        {
            ActiveRuns.Leave(solution);
        }
    }

    [Fact]
    public async Task EnterAsync_WhileARunHoldsTheSolution_WaitsForItToLeaveAndThenEnters()
    {
        var solution = Path.Combine(Path.GetTempPath(), "terse-active-queue.slnx");
        using var stop = new CancellationTokenSource();

        Assert.True(ActiveRuns.TryEnter(solution, "run_tests", string.Empty, out _, stop));
        var queued = ActiveRuns.EnterAsync(solution, "build", string.Empty, stop, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        try
        {
            Assert.False(queued.IsCompleted);
            ActiveRuns.Leave(solution);

            Assert.Null(await queued);
            Assert.Equal("build", ActiveRuns.Holding(solution)?.Tool);
        }
        finally
        {
            ActiveRuns.Leave(solution);
        }
    }

    [Fact]
    public async Task EnterAsync_WhenTheHolderOutlastsTheWait_AnswersTheHolderAndLeavesItHolding()
    {
        var solution = Path.Combine(Path.GetTempPath(), "terse-active-outlast.slnx");
        using var stop = new CancellationTokenSource();

        Assert.True(ActiveRuns.TryEnter(solution, "run_tests", "Selection.Core.Tests", out _, stop));
        try
        {
            var holder = await ActiveRuns.EnterAsync(solution, "build", string.Empty, stop, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

            Assert.Equal("run_tests", holder?.Tool);
            Assert.Equal("run_tests", ActiveRuns.Holding(solution)?.Tool);
            Assert.Contains("queued behind it for its whole 1s timeoutSeconds", Errors.RunInFlight("build", holder.GetValueOrDefault(), TimeSpan.FromSeconds(1)).Render(), StringComparison.Ordinal);
        }
        finally
        {
            ActiveRuns.Leave(solution);
        }
    }

    [Fact]
    public async Task EnterAsync_WithNoWait_RefusesAtOnceAndNamesTheHolder()
    {
        var solution = Path.Combine(Path.GetTempPath(), "terse-active-nowait.slnx");
        using var stop = new CancellationTokenSource();

        Assert.True(ActiveRuns.TryEnter(solution, "run_tests", string.Empty, out _, stop));
        try
        {
            var refused = ActiveRuns.EnterAsync(solution, "build", string.Empty, stop, TimeSpan.Zero, TestContext.Current.CancellationToken);

            Assert.True(refused.IsCompleted);
            Assert.Equal("run_tests", (await refused)?.Tool);
        }
        finally
        {
            ActiveRuns.Leave(solution);
        }
    }
}
