namespace TerseSharp.Server;

internal static class HistoryRefresh
{
    public static async Task RunAsync(string root, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        if (!HistoryHealth.Wanted(await PolicyCache.ForAsync(root, cancellationToken).ConfigureAwait(false), ids))
            return;

        if (await HeadAsync(root, cancellationToken).ConfigureAwait(false) is not { } head || HistoryHealth.Holds(root, head))
            return;

        if (await GitRunner.ReadAsync(root, HistoryHealth.LogArguments, cancellationToken).ConfigureAwait(false) is { IsOk: true, Value: { } log })
            HistoryHealth.Remember(root, head, log);
    }

    private static async Task<string?> HeadAsync(string root, CancellationToken cancellationToken) =>
        await GitRunner.ReadAsync(root, ["--no-optional-locks", "rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken).ConfigureAwait(false) is { IsOk: true, Value: { } head }
            ? head.Trim()
            : null;
}
