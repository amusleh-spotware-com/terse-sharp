using System.Text;

namespace TerseSharp.Core;

public static class UntrackedFile
{
    public const long MaxBytes = 8L * 1024 * 1024;

    private const int BinarySniff = 8000;

    public static async Task<int?> LinesAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await ReadAsync(path, cancellationToken).ConfigureAwait(false);

        return bytes is null || IsBinary(bytes) ? null : Lines(bytes);
    }

    public static async Task<string> DiffAsync(string root, string relative, CancellationToken cancellationToken)
    {
        var header = Header(relative);
        var bytes = await ReadAsync(Path.Join(root, relative), cancellationToken).ConfigureAwait(false);

        return bytes switch
        {
            null => header + string.Create(CultureInfo.InvariantCulture, $"untracked file over {MaxBytes / (1024 * 1024)} MB not shown - read_text path=\"{relative}\" reads it in ranges\n"),
            { Length: 0 } => header,
            _ when IsBinary(bytes) => header + string.Create(CultureInfo.InvariantCulture, $"Binary files /dev/null and b/{relative} differ\n"),
            _ => Added(header, relative, Encoding.UTF8.GetString(bytes)),
        };
    }

    public static int Lines(ReadOnlySpan<byte> bytes) => bytes.IsEmpty
        ? 0
        : bytes.Count((byte)'\n') + (bytes[^1] is (byte)'\n' ? 0 : 1);

    public static bool IsBinary(ReadOnlySpan<byte> bytes) => bytes[..Math.Min(bytes.Length, BinarySniff)].Contains((byte)0);

    private static async Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);

        return file.Exists && file.Length <= MaxBytes
            ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)
            : null;
    }

    private static string Header(string relative) => string.Create(
        CultureInfo.InvariantCulture,
        $"diff --git a/{relative} b/{relative}\nnew file mode 100644\n");

    private static string Added(string header, string relative, string text)
    {
        var body = text.AsSpan();
        var terminated = body[^1] is '\n';
        var lines = terminated ? body[..^1] : body;
        var builder = new StringBuilder(header, header.Length + text.Length + (relative.Length * 2) + 64);

        builder.Append(CultureInfo.InvariantCulture, $"--- /dev/null\n+++ b/{relative}\n@@ -0,0 +1{Range(lines.Count('\n') + 1)} @@\n");

        foreach (var line in lines.Split('\n'))
            builder.Append('+').Append(lines[line].TrimEnd('\r')).Append('\n');

        return (terminated ? builder : builder.Append("\\ No newline at end of file\n")).ToString();
    }

    private static string Range(int count) => count is 1 ? string.Empty : "," + count.ToString(CultureInfo.InvariantCulture);
}
