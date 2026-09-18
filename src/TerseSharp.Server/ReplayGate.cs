using System.Diagnostics;
using System.Text;

namespace TerseSharp.Server;

public sealed class ReplayGate(ToolContext context, UnchangedRun unchanged)
{
    public async Task<string> ReplayedAsync(string tool, string key, bool force, Func<Task<string>> run, CancellationToken cancellationToken)
    {
        await context.ReadyAsync().ConfigureAwait(false);

        if (!await RunStamp.SyncedAsync(context.Registry, cancellationToken).ConfigureAwait(false))
            return await run().ConfigureAwait(false);

        if (RunStamp.Taken(context.Registry, out _) is not { } stamp)
            return await run().ConfigureAwait(false);

        if (!force && unchanged.Verbatim(tool, key, stamp, Stopwatch.GetTimestamp()) is { } previous)
            return previous;

        var text = await run().ConfigureAwait(false);

        if (!text.StartsWith("ERROR", StringComparison.Ordinal) && RunStamp.Taken(context.Registry, out _) is { } settled)
            unchanged.Remember(key, settled, text, Stopwatch.GetTimestamp());

        return text;
    }

    public const char KeySeparator = (char)31;

    public static string Key(string tool, params ReadOnlySpan<string?> parts)
    {
        var key = new StringBuilder(tool, 128);

        foreach (var part in parts)
            key.Append(KeySeparator).Append(part);

        return key.ToString();
    }
}
