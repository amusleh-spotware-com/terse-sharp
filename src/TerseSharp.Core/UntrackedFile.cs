using System.Buffers;
using System.Text;

namespace TerseSharp.Core;

public static class UntrackedFile
{
    public const long MaxBytes = 8L * 1024 * 1024;

    private const int BinarySniff = 8000;

    private const int BufferBytes = 64 * 1024;

    public static async Task<int?> LinesAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = Open(path);

            return stream.Length > MaxBytes ? null : await CountedAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task<string> DiffAsync(string root, string relative, CancellationToken cancellationToken)
    {
        var header = Header(relative);
        var (bytes, unread) = await ReadAsync(Path.Join(root, relative), cancellationToken).ConfigureAwait(false);

        return bytes switch
        {
            null => header + string.Create(CultureInfo.InvariantCulture, $"{unread} - read_text path=\"{relative}\" reads it in ranges\n"),
            { Length: 0 } => header,
            _ when IsBinary(bytes) => header + string.Create(CultureInfo.InvariantCulture, $"Binary files /dev/null and b/{relative} differ\n"),
            _ => Added(header, relative, Encoding.UTF8.GetString(bytes)),
        };
    }

    public static int Lines(ReadOnlySpan<byte> bytes) => bytes.IsEmpty
        ? 0
        : bytes.Count((byte)'\n') + (bytes[^1] is (byte)'\n' ? 0 : 1);

    public static bool IsBinary(ReadOnlySpan<byte> bytes) => bytes[..Math.Min(bytes.Length, BinarySniff)].Contains((byte)0);

    private static FileStream Open(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        BufferBytes,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task<int?> CountedAsync(FileStream stream, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferBytes);

        try
        {
            var tally = new LineTally();
            int read;

            while ((read = await stream.ReadAsync(buffer.AsMemory(0, BufferBytes), cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (!tally.Add(buffer.AsSpan(0, read)))
                    return null;
            }

            return tally.Lines;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<(byte[]? Bytes, string Unread)> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = Open(path);

            if (stream.Length > MaxBytes)
                return (null, string.Create(CultureInfo.InvariantCulture, $"untracked file over {MaxBytes / (1024 * 1024)} MB not shown"));

            var bytes = new byte[stream.Length];

            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);

            return (bytes, string.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, "untracked file could not be read (" + exception.GetType().Name + ")");
        }
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

    private struct LineTally
    {
        private long seen;
        private int newlines;
        private byte last;

        public readonly int Lines => seen is 0 ? 0 : newlines + (last is (byte)'\n' ? 0 : 1);

        public bool Add(ReadOnlySpan<byte> chunk)
        {
            if (seen < BinarySniff && chunk[..(int)Math.Min(chunk.Length, BinarySniff - seen)].Contains((byte)0))
                return false;

            newlines += chunk.Count((byte)'\n');
            last = chunk[^1];
            seen += chunk.Length;

            return true;
        }
    }
}
