using System.Text;

namespace TerseSharp.Server;

public static class RunStamp
{
    public static string? Taken(WorkspaceRegistry registry, out string? unavailable)
    {
        var loaded = registry.All();
        var stamp = new StringBuilder(128);

        stamp.Append(CultureInfo.InvariantCulture, $"pulse={EditPulse.Changed} loaded={loaded.Count}");

        foreach (var workspace in loaded.OrderBy(entry => entry.Root, StringComparer.Ordinal))
        {
            var sync = workspace.Sync;

            if (sync.State is not WatchState.Active || sync.Gaps > 0)
            {
                unavailable = string.Create(CultureInfo.InvariantCulture, $"{workspace.Root} watch={sync.State} gaps={sync.Gaps}");

                return null;
            }

            stamp.Append(' ')
                .Append(workspace.Root)
                .Append('@')
                .Append(workspace.LoadedUtc.ToString("O", CultureInfo.InvariantCulture))
                .Append('=')
                .Append(sync.Generations.ToString());
        }

        unavailable = null;

        return stamp.ToString();
    }

    public static async Task<bool> SyncedAsync(WorkspaceRegistry registry, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var workspace in registry.All())
                await workspace.Sync.SyncAsync(workspace, null, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }
}
