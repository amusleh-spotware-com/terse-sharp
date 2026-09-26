using System.Collections.Concurrent;
using System.Diagnostics;

namespace TerseSharp.Server;

public sealed class DetachedRuns : IDisposable
{
    private const int MaxRuns = 16;
    private readonly ConcurrentDictionary<string, DetachedRun> runs = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource stopping = new();
    private int next;

    public string Start(Func<CancellationToken, Task<string>> run)
    {
        var id = string.Create(CultureInfo.InvariantCulture, $"t{Interlocked.Increment(ref next)}");
        var token = stopping.Token;

        runs[id] = new DetachedRun(Task.Run(() => ToolBoundary.RunAsync(() => run(token)), CancellationToken.None), Stopwatch.GetTimestamp());
        Prune();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"run_tests DETACHED id={id}\nnext: run_tests status=\"{id}\" answers RUNNING until the verdict is ready, then the verdict itself");
    }

    public async Task<string> StatusAsync(string id) =>
        !runs.TryGetValue(id, out var run) ? Unknown(id)
        : run.Task.IsCompleted ? await run.Task.ConfigureAwait(false)
        : Running(id, run.Started);

    public void Dispose()
    {
        stopping.Cancel();
        stopping.Dispose();
    }

    private static string Running(string id, long started) => string.Create(
        CultureInfo.InvariantCulture,
        $"run_tests RUNNING id={id} {Stopwatch.GetElapsedTime(started).TotalSeconds:F0}s\nnext: run_tests status=\"{id}\" again later - the run finishes without being polled");

    private static string Unknown(string id) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"no detached run has id '{id}' in this server - ids live only as long as the process that answered them"),
        "pass the id a run_tests detach=true call answered, or start the run again with detach=true").Render();

    private void Prune()
    {
        if (runs.Count <= MaxRuns)
            return;

        foreach (var finished in runs.Where(pair => pair.Value.Task.IsCompleted).OrderBy(pair => pair.Value.Started).Take(runs.Count - MaxRuns).ToList())
            runs.TryRemove(finished.Key, out _);
    }

    private readonly record struct DetachedRun(Task<string> Task, long Started);
}
