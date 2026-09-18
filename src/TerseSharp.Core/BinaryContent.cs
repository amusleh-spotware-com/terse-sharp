using System.Buffers;
using System.Text;

namespace TerseSharp.Core;

public static class BinaryContent
{
    private const int ProbeBytes = 8000;

    public static async Task<BinaryProbe> ProbeAsync(string fullPath, string displayPath, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ProbeBytes);

        try
        {
            var length = new FileInfo(fullPath).Length;

            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                ProbeBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var read = await stream.ReadAtLeastAsync(buffer.AsMemory(0, ProbeBytes), ProbeBytes, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);

            return Probed(buffer.AsSpan(0, read), displayPath, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Result<string> Binary(string path, long length) => Result.Fail<string>(Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"'{path}' looks binary ({length} bytes)"),
        "read_text serves text only"));

    private static bool HasNullCodeUnit(ReadOnlySpan<byte> probe)
    {
        var mark = ByteOrderMark.Detect(probe);
        var units = probe[Math.Min(mark.Length, probe.Length)..];

        if (mark.CodeUnit is 1)
            return units.Contains((byte)0) && Utf16WithoutMark.Detect(probe) is null;

        return HasZeroUnit(units, mark.CodeUnit);
    }

    private static bool HasZeroUnit(ReadOnlySpan<byte> units, int size)
    {
        for (var offset = 0; offset + size <= units.Length; offset += size)
        {
            if (!units.Slice(offset, size).ContainsAnyExcept((byte)0))
                return true;
        }

        return false;
    }

    public readonly record struct BinaryProbe(Result<string>? Refusal, Encoding? Utf16);

    private static BinaryProbe Probed(ReadOnlySpan<byte> probe, string displayPath, long length) =>
        HasNullCodeUnit(probe)
            ? new BinaryProbe(Binary(displayPath, length), null)
            : new BinaryProbe(null, Utf16WithoutMark.Detect(probe));
}
