using System.Text;

namespace TerseSharp.Core;

public static class AtomicWrite
{
    public static Task TextAsync(string path, string content, bool workspaceDocument, CancellationToken cancellationToken = default) =>
        PersistAsync(path, content, workspaceDocument, cancellationToken);

    public static Encoding EncodingOf(string path)
    {
        Span<byte> head = stackalloc byte[4];

        return ByteOrderMark.EncodingOf(head[..Head(path, head)]);
    }

    private static async Task PersistAsync(string path, string content, bool workspaceDocument, CancellationToken cancellationToken)
    {
        var temporary = path + ".terse-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";

        EnsureDirectory(path);

        try
        {
            await WriteAsync(temporary, content, EncodingOf(path), cancellationToken).ConfigureAwait(false);
            await MoveAsync(temporary, path, cancellationToken).ConfigureAwait(false);

            if (workspaceDocument)
                EditPulse.Bump(path);
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

    private static int Head(string path, Span<byte> head)
    {
        try
        {
            using var stream = File.OpenRead(path);

            return stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
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
            await MoveAsync(temporary, path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
    private const int MoveAttempts = 8;
    private const int MoveBackoffMilliseconds = 20;

    private static Task BackoffAsync(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(attempt * MoveBackoffMilliseconds, cancellationToken);

    private static async Task MoveAsync(string temporary, string path, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < MoveAttempts)
            {
                await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt < MoveAttempts)
            {
                await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static Task TextAsync(string path, string content, CancellationToken cancellationToken = default) =>
        PersistAsync(path, content, workspaceDocument: true, cancellationToken);
}
