using System.Security.Cryptography;
using System.Text;

namespace TerseSharp.Server;

internal static class NudgeHoldOut
{
    internal const string LogFileName = "batch-nudge.log";

    private const int MaxStackBytes = 256;

    internal static NudgeArm Assign(ReadOnlySpan<char> sessionId) => IsHeldOut(sessionId) ? NudgeArm.Held : NudgeArm.Nudged;

    internal static bool IsHeldOut(ReadOnlySpan<char> sessionId)
    {
        var length = Encoding.UTF8.GetByteCount(sessionId);
        var utf8 = length <= MaxStackBytes ? stackalloc byte[MaxStackBytes] : new byte[length];
        var written = Encoding.UTF8.GetBytes(sessionId, utf8);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(utf8[..written], digest);

        return (digest[0] & 1) is 1;
    }

    internal static async Task RecordAsync(string home, BatchVerdict verdict, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Directory.CreateDirectory(Path.Combine(home, ".terse"));
            await using var stream = new FileStream(Path.Combine(directory.FullName, LogFileName), FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);

            await stream.WriteAsync(Encoding.UTF8.GetBytes(Line(verdict)), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Line(BatchVerdict verdict) => string.Create(
        CultureInfo.InvariantCulture,
        $"{DateTime.UtcNow:O} {verdict.SessionId} {verdict.ToolUseId} {(verdict.Arm is NudgeArm.Held ? "held" : "nudged")}\n");
}

internal enum NudgeArm
{
    Nudged,
    Held,
}

internal readonly record struct BatchVerdict(string SessionId, string ToolUseId, NudgeArm Arm);
