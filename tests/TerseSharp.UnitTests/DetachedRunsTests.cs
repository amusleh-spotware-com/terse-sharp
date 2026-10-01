using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class DetachedRunsTests
{
    private const string Started = "run_tests DETACHED id=";

    [Fact]
    public async Task Status_WhilePending_AnswersRunning_AndOnceFinished_AnswersTheVerdict()
    {
        await using var runs = new DetachedRuns();
        var verdict = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = runs.Start(_ => verdict.Task);
        var id = answer[Started.Length..answer.IndexOf('\n', StringComparison.Ordinal)];

        Assert.StartsWith(Started, answer, StringComparison.Ordinal);
        Assert.StartsWith("run_tests RUNNING id=" + id + " ", await runs.StatusAsync(id, TimeSpan.Zero, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        verdict.SetResult("run_tests PASSED  passed=1 total=1");

        Assert.StartsWith("run_tests PASSED  passed=1 total=1", await SettledAsync(runs, id), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_ForARunThatThrew_AnswersTheRenderedErrorRatherThanRunningForever()
    {
        await using var runs = new DetachedRuns();
        var answer = runs.Start(_ => Task.FromException<string>(new InvalidOperationException("boom")));
        var settled = await SettledAsync(runs, answer[Started.Length..answer.IndexOf('\n', StringComparison.Ordinal)]);

        Assert.StartsWith("ERROR", settled, StringComparison.Ordinal);
        Assert.Contains("boom", settled, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_ForAnIdNeverIssued_IsRefusedNamingItWithARemedy()
    {
        await using var runs = new DetachedRuns();
        var text = await runs.StatusAsync("t42", TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("'t42'", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_WithAWait_AnswersTheVerdictTheMomentTheRunFinishes()
    {
        await using var runs = new DetachedRuns();
        var verdict = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = runs.StatusAsync(IdOf(runs.Start(_ => verdict.Task)), TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        Assert.False(waiting.IsCompleted);

        verdict.SetResult("run_tests PASSED  passed=1 total=1");

        Assert.StartsWith("run_tests PASSED  passed=1 total=1", await waiting, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_WhenTheWaitRunsOutFirst_AnswersRunning_AndTheRunStillLands()
    {
        await using var runs = new DetachedRuns();
        var verdict = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = IdOf(runs.Start(_ => verdict.Task));

        Assert.StartsWith("run_tests RUNNING id=" + id + " ", await runs.StatusAsync(id, TimeSpan.FromMilliseconds(1), TestContext.Current.CancellationToken), StringComparison.Ordinal);

        verdict.SetResult("run_tests PASSED  passed=1 total=1");

        Assert.StartsWith("run_tests PASSED", await runs.StatusAsync(id, TimeSpan.FromHours(1), TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_WhenTheWaitingCallIsCancelled_AnswersRunningWithoutStoppingTheRun()
    {
        await using var runs = new DetachedRuns();
        using var cancelled = new CancellationTokenSource();
        var verdict = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = IdOf(runs.Start(_ => verdict.Task));
        var waiting = runs.StatusAsync(id, TimeSpan.FromHours(1), cancelled.Token);

        await cancelled.CancelAsync();
        Assert.StartsWith("run_tests RUNNING id=" + id + " ", await waiting, StringComparison.Ordinal);

        verdict.SetResult("run_tests PASSED  passed=1 total=1");

        Assert.StartsWith("run_tests PASSED", await runs.StatusAsync(id, TimeSpan.FromHours(1), TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAndRunning_SayNoNotificationArrives_AndNameTheWaitingCall()
    {
        await using var runs = new DetachedRuns();
        var answer = runs.Start(_ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously).Task);
        var waitingCall = "next: run_tests status=\"" + IdOf(answer) + "\" waitSeconds=3600 before ending the turn - no notification arrives when a detached run finishes";

        Assert.Contains(waitingCall, answer, StringComparison.Ordinal);
        Assert.Contains(waitingCall, await runs.StatusAsync(IdOf(answer), TimeSpan.Zero, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    private static string IdOf(string answer) => answer[Started.Length..answer.IndexOf('\n', StringComparison.Ordinal)];

    private static async Task<string> SettledAsync(DetachedRuns runs, string id)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var text = await runs.StatusAsync(id, TimeSpan.Zero, TestContext.Current.CancellationToken);

            if (!text.StartsWith("run_tests RUNNING", StringComparison.Ordinal))
                return text;

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        return "still RUNNING after 2 s";
    }
}
