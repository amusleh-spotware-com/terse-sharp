using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class DetachedRunsTests
{
    private const string Started = "run_tests DETACHED id=";

    [Fact]
    public async Task Status_WhilePending_AnswersRunning_AndOnceFinished_AnswersTheVerdict()
    {
        using var runs = new DetachedRuns();
        var verdict = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = runs.Start(_ => verdict.Task);
        var id = answer[Started.Length..answer.IndexOf('\n', StringComparison.Ordinal)];

        Assert.StartsWith(Started, answer, StringComparison.Ordinal);
        Assert.StartsWith("run_tests RUNNING id=" + id + " ", await runs.StatusAsync(id), StringComparison.Ordinal);

        verdict.SetResult("run_tests PASSED  passed=1 total=1");

        Assert.StartsWith("run_tests PASSED  passed=1 total=1", await SettledAsync(runs, id), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_ForARunThatThrew_AnswersTheRenderedErrorRatherThanRunningForever()
    {
        using var runs = new DetachedRuns();
        var answer = runs.Start(_ => Task.FromException<string>(new InvalidOperationException("boom")));
        var settled = await SettledAsync(runs, answer[Started.Length..answer.IndexOf('\n', StringComparison.Ordinal)]);

        Assert.StartsWith("ERROR", settled, StringComparison.Ordinal);
        Assert.Contains("boom", settled, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_ForAnIdNeverIssued_IsRefusedNamingItWithARemedy()
    {
        using var runs = new DetachedRuns();
        var text = await runs.StatusAsync("t42");

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("'t42'", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    private static async Task<string> SettledAsync(DetachedRuns runs, string id)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var text = await runs.StatusAsync(id);

            if (!text.StartsWith("run_tests RUNNING", StringComparison.Ordinal))
                return text;

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        return "still RUNNING after 2 s";
    }
}
