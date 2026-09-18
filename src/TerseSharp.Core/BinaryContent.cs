namespace TerseSharp.Core;

public static class BinaryContent
{
    private const int ProbeBytes = 8000;

    public static Result<string>? Reject(string fullPath, string displayPath) =>
        LooksBinary(fullPath) ? Binary(displayPath, new FileInfo(fullPath).Length) : null;

    private static Result<string> Binary(string path, long length) => Result.Fail<string>(Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"'{path}' looks binary ({length} bytes)"),
        "read_text serves text only"));

    private static bool LooksBinary(string fullPath)
    {
        using var stream = File.OpenRead(fullPath);
        Span<byte> probe = stackalloc byte[ProbeBytes];
        var read = stream.ReadAtLeast(probe, ProbeBytes, throwOnEndOfStream: false);

        return HasNullCodeUnit(probe[..read]);
    }

    private static bool HasNullCodeUnit(ReadOnlySpan<byte> probe)
    {
        var mark = ByteOrderMark.Detect(probe);
        var units = probe[Math.Min(mark.Length, probe.Length)..];

        return mark.CodeUnit is 1 ? units.Contains((byte)0) : HasZeroUnit(units, mark.CodeUnit);
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
}
