using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace TerseSharp.Server;

internal static class NudgeHoldOut
{
    internal const string LogFileName = "batch-nudge.log";

    internal const string Unidentified = "-";

    private const int MaxStackBytes = 256;

    private static readonly SearchValues<char> Blanks = SearchValues.Create(" \t\r\n");

    internal static BatchVerdict Assign(string? toolUseId) =>
        toolUseId is { Length: > 0 } && !toolUseId.AsSpan().ContainsAny(Blanks)
            ? new(toolUseId, IsHeldOut(toolUseId) ? NudgeArm.Held : NudgeArm.Nudged)
            : new(Unidentified, NudgeArm.Nudged);

    internal static bool IsHeldOut(ReadOnlySpan<char> toolUseId)
    {
        var length = Encoding.UTF8.GetByteCount(toolUseId);
        var utf8 = length <= MaxStackBytes ? stackalloc byte[MaxStackBytes] : new byte[length];
        var written = Encoding.UTF8.GetBytes(toolUseId, utf8);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(utf8[..written], digest);

        return (digest[0] & 1) is 1;
    }

    internal static async Task RecordAsync(string home, BatchVerdict verdict, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Directory.CreateDirectory(Path.Combine(home, ".terse"));
            await File.AppendAllTextAsync(Path.Combine(directory.FullName, LogFileName), Line(verdict), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Line(BatchVerdict verdict) => string.Create(
        CultureInfo.InvariantCulture,
        $"{DateTime.UtcNow:O} {verdict.ToolUseId} {(verdict.Arm is NudgeArm.Held ? "held" : "nudged")}\n");
}

internal enum NudgeArm
{
    Nudged,
    Held,
}

internal readonly record struct BatchVerdict(string ToolUseId, NudgeArm Arm);
