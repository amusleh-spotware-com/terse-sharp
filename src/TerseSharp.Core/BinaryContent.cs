namespace TerseSharp.Core;

public static class BinaryContent
{
    private const int MaxBytes = 64 * 1024;
    private const int ProbeBytes = 8000;

    public static Result<string>? Reject(string fullPath, string displayPath)
    {
        var file = new FileInfo(fullPath);

        if (file.Length > MaxBytes)
            return TooLarge(displayPath, file.Length);

        return LooksBinary(fullPath) ? Binary(displayPath, file.Length) : null;
    }

    private static Result<string> TooLarge(string path, long length) => Result.Fail<string>(Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"'{path}' is {length} bytes, over the {MaxBytes} byte cap"),
        "pass startLine and endLine to read a range"));

    private static Result<string> Binary(string path, long length) => Result.Fail<string>(Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"'{path}' looks binary ({length} bytes)"),
        "read_text serves text only"));

    private static bool LooksBinary(string fullPath)
    {
        using var stream = File.OpenRead(fullPath);
        Span<byte> probe = stackalloc byte[ProbeBytes];
        var read = stream.ReadAtLeast(probe, ProbeBytes, throwOnEndOfStream: false);

        return probe[..read].Contains((byte)0);
    }
}
