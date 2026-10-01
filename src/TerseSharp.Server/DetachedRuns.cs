using System.Collections.Concurrent;
using System.Diagnostics;

namespace TerseSharp.Server;

public sealed class DetachedRuns : IDisposable, IAsyncDisposable
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
            $"run_tests DETACHED id={id}\n{Unnotified(id)}");
    }

    public async Task<string> StatusAsync(string id, TimeSpan wait, CancellationToken cancellationToken)
    {
        if (!runs.TryGetValue(id, out var run))
            return Unknown(id);

        await SettledAsync(run.Task, wait, cancellationToken).ConfigureAwait(false);

        return run.Task.IsCompleted ? await run.Task.ConfigureAwait(false) : Running(id, run.Started);
    }

    public void Dispose()
    {
        stopping.Cancel();
        stopping.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync().ConfigureAwait(false);
        stopping.Dispose();
    }

    private static string Running(string id, long started) => string.Create(
        CultureInfo.InvariantCulture,
        $"run_tests RUNNING id={id} {Stopwatch.GetElapsedTime(started).TotalSeconds:F0}s\n{Unnotified(id)}");

    private static string Unnotified(string id) => string.Create(
        CultureInfo.InvariantCulture,
        $"next: run_tests status=\"{id}\" waitSeconds={DotnetRunner.MaxTimeoutSeconds} before ending the turn - no notification arrives when a detached run finishes; that call answers the moment the verdict lands, and a client that backgrounds a long call notifies you when it returns; pass a shorter waitSeconds if your client times out long calls");

    private static async Task SettledAsync(Task run, TimeSpan wait, CancellationToken cancellationToken)
    {
        if (run.IsCompleted || wait <= TimeSpan.Zero)
            return;

        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await Task.WhenAny(run, Task.Delay(wait, expiry.Token)).ConfigureAwait(false);
        await expiry.CancelAsync().ConfigureAwait(false);
    }

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
