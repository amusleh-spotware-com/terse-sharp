using System.Collections.Concurrent;

namespace TerseSharp.Core;

internal static class ProjectFileGate
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    public static async ValueTask<Held> EnterAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        if (paths.Count is 0)
            return default;

        var taken = new List<SemaphoreSlim>(paths.Count);

        try
        {
            foreach (var path in Ordered(paths))
                await EnteredAsync(taken, path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Release(taken);

            throw;
        }

        return new Held(taken);
    }

    private static async Task EnteredAsync(List<SemaphoreSlim> taken, string path, CancellationToken cancellationToken)
    {
        var gate = Gates.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        taken.Add(gate);
    }

    private static string[] Ordered(IReadOnlyList<string> paths) =>
        [.. paths.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];

    private static void Release(List<SemaphoreSlim> taken)
    {
        foreach (var gate in taken)
            gate.Release();
    }

    public readonly record struct Held(List<SemaphoreSlim>? Taken) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            if (Taken is { } gates)
                Release(gates);

            return ValueTask.CompletedTask;
        }
    }
}
