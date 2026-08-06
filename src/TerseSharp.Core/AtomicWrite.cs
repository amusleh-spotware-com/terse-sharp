using System.Text;

namespace TerseSharp.Core;

public static class AtomicWrite
{
    public static Task TextAsync(string path, string content, CancellationToken cancellationToken = default) =>
        PersistAsync(path, content, cancellationToken);

    public static Encoding EncodingOf(string path) => new UTF8Encoding(HasByteOrderMark(path));

    private static async Task PersistAsync(string path, string content, CancellationToken cancellationToken)
    {
        var temporary = path + ".terse-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";

        EnsureDirectory(path);

        try
        {
            await WriteAsync(temporary, content, EncodingOf(path), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void EnsureDirectory(string path)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } directory && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
    }

    private static bool HasByteOrderMark(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var head = new byte[3];

            return stream.ReadAtLeast(head, 3, throwOnEndOfStream: false) is 3
                && head[0] is 0xEF
                && head[1] is 0xBB
                && head[2] is 0xBF;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task WriteAsync(string temporary, string content, Encoding encoding, CancellationToken cancellationToken)
    {
        var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);

        await using (stream.ConfigureAwait(false))
        {
            var writer = new StreamWriter(stream, encoding);

            await using (writer.ConfigureAwait(false))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static async Task BytesAsync(string path, byte[] content, CancellationToken cancellationToken = default)
    {
        var temporary = path + ".terse-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".bytes.tmp";

        EnsureDirectory(path);

        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
