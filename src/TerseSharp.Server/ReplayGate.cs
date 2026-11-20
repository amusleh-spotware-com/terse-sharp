using System.Diagnostics;
using System.Text;
using TerseSharp.Server.Tools;

namespace TerseSharp.Server;

public sealed class ReplayGate(ToolContext context, UnchangedRun unchanged)
{
    public Task<string> ReplayedAsync(string tool, string key, bool force, Func<Task<string>> run, CancellationToken cancellationToken) =>
        ReplayedAsync(tool, new ReplayKey(key, null), force, run, cancellationToken);

    public async Task<string> ReplayedAsync(string tool, ReplayKey key, bool force, Func<Task<string>> run, CancellationToken cancellationToken)
    {
        await context.ReadyAsync().ConfigureAwait(false);

        if (await StampedAsync(key, force, cancellationToken).ConfigureAwait(false) is not { } stamped)
            return await run().ConfigureAwait(false);

        if (!force && unchanged.Verbatim(tool, stamped.Keyed, stamped.Stamp, Stopwatch.GetTimestamp()) is { } previous)
            return previous;

        var text = await run().ConfigureAwait(false);

        await RememberAsync(key, stamped.Keyed, text, cancellationToken).ConfigureAwait(false);

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

    internal static bool Rememberable(string text) =>
        !text.StartsWith("ERROR", StringComparison.Ordinal) && !text.Contains("\nPARTIAL ", StringComparison.Ordinal);

    public static ReplayKey GitKeyed(string key, string? baseRef, bool defaulted) => new(key, baseRef switch
    {
        { Length: > 0 } => baseRef,
        null when defaulted => "HEAD",
        _ => null,
    });

    internal static string? GitKey(string key, string gitRef, ReadOnlySpan<string?> states)
    {
        if (states.IsEmpty)
            return null;

        var keyed = new StringBuilder(key, key.Length + 160);

        keyed.Append(KeySeparator).Append("git=").Append(gitRef);

        foreach (var state in states)
        {
            if (state is null)
                return null;

            keyed.Append(KeySeparator).Append(state);
        }

        return keyed.ToString();
    }

    private async Task<Stamped?> StampedAsync(ReplayKey key, bool force, CancellationToken cancellationToken)
    {
        var gitForced = force && key.GitRef is not null;

        if (gitForced || !await RunStamp.SyncedAsync(context.Registry, cancellationToken).ConfigureAwait(false))
            return null;

        if (RunStamp.Taken(context.Registry, out _) is not { } stamp)
            return null;

        return await GitKeyedAsync(key, cancellationToken).ConfigureAwait(false) is { } keyed ? new Stamped(stamp, keyed) : null;
    }

    private async Task RememberAsync(ReplayKey key, string keyed, string text, CancellationToken cancellationToken)
    {
        if (!Rememberable(text) || RunStamp.Taken(context.Registry, out _) is not { } settled)
            return;

        if (string.Equals(await GitKeyedAsync(key, cancellationToken).ConfigureAwait(false), keyed, StringComparison.Ordinal))
            unchanged.Remember(keyed, settled, ToolContext.Unqueued(text), Stopwatch.GetTimestamp());
    }

    private async Task<string?> GitKeyedAsync(ReplayKey key, CancellationToken cancellationToken)
    {
        if (key.GitRef is not { } gitRef)
            return key.Value;

        var loaded = context.Registry.All();
        var states = new string?[loaded.Count];
        var index = 0;

        foreach (var workspace in loaded.OrderBy(entry => entry.Root, StringComparer.Ordinal))
            states[index++] = await GitStateAsync(workspace, gitRef, cancellationToken).ConfigureAwait(false);

        return GitKey(key.Value, gitRef, states);
    }

    private static async Task<string?> GitStateAsync(LoadedWorkspace workspace, string gitRef, CancellationToken cancellationToken)
    {
        var stamp = await GitTools.GitStampAsync(workspace, cancellationToken).ConfigureAwait(false);

        if (stamp.Value is null)
            return null;

        var commit = await GitRunner.ReadAsync(workspace.Root, ["--no-optional-locks", "rev-parse", "--verify", "--quiet", "--end-of-options", gitRef + "^{commit}"], cancellationToken).ConfigureAwait(false);

        return commit is { IsOk: true, Value: { } sha } ? string.Create(CultureInfo.InvariantCulture, $"{workspace.Root}@{stamp.Value} {gitRef}={sha.AsSpan().Trim()}") : null;
    }

    public readonly record struct ReplayKey(string Value, string? GitRef);

    private readonly record struct Stamped(string Stamp, string Keyed);
}
