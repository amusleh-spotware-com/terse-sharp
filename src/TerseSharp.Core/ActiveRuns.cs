using System.Diagnostics;

namespace TerseSharp.Core;

public readonly record struct ActiveRun(string Tool, string Detail, long StartedTicks);

public static class ActiveRuns
{
    private const string Draining = "its processes are still exiting, so the next build or test call may answer RunInFlight for a few more seconds";
    private static readonly TimeSpan Drain = TimeSpan.FromSeconds(30);
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, HeldRun> Running = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryEnter(string solutionPath, string tool, string detail, out ActiveRun holder, CancellationTokenSource stop)
    {
        lock (Gate)
        {
            if (Running.TryGetValue(solutionPath, out var held))
            {
                holder = held.Holder;

                return false;
            }

            holder = new ActiveRun(tool, detail, Stopwatch.GetTimestamp());
            Running[solutionPath] = new HeldRun(holder, stop, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

            return true;
        }
    }

    public static void Leave(string solutionPath)
    {
        lock (Gate)
        {
            if (Running.Remove(solutionPath, out var held))
                held.Released.TrySetResult();
        }
    }

    public static ActiveRun? Holding(string solutionPath)
    {
        lock (Gate)
            return Running.TryGetValue(solutionPath, out var held) ? held.Holder : null;
    }

    public static async Task<string> CancelAsync(string solutionPath, CancellationToken cancellationToken)
    {
        if (Held(solutionPath) is not { } held)
            return NothingToCancel();

        await StopAsync(held.Stop).ConfigureAwait(false);

        var seconds = Age(held.Holder).TotalSeconds;
        var freed = await FreedAsync(held.Released.Task, cancellationToken).ConfigureAwait(false);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"CANCELLED {held.Holder.Tool}{(held.Holder.Detail.Length is 0 ? string.Empty : " ")}{held.Holder.Detail} after {seconds:F0}s - {(freed ? "the solution is free" : Draining)}");
    }

    public static TimeSpan Age(ActiveRun holder) => Stopwatch.GetElapsedTime(holder.StartedTicks);

    private static HeldRun? Held(string solutionPath)
    {
        lock (Gate)
            return Running.TryGetValue(solutionPath, out var held) ? held : null;
    }

    private static async Task StopAsync(CancellationTokenSource stop)
    {
        try
        {
            await stop.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task<bool> FreedAsync(Task released, CancellationToken cancellationToken)
    {
        try
        {
            await released.WaitAsync(Drain, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static string NothingToCancel() => Errors.Invalid(
        "nothing to cancel - no build or test run is in flight for this solution in this process",
        "start the run you need; a run over another solution is cancelled by naming it with workspace=").Render();

    private sealed record HeldRun(ActiveRun Holder, CancellationTokenSource Stop, TaskCompletionSource Released);
}
