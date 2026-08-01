using System.Text;

namespace TerseSharp.Core;

public static class AtomicWrite
{
    public static void Text(string path, string content)
    {
        var temporary = path + ".terse-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";

        try
        {
            Persist(temporary, content, EncodingOf(path));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static Encoding EncodingOf(string path) => new UTF8Encoding(HasByteOrderMark(path));

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

    private static void Persist(string temporary, string content, Encoding encoding)
    {
        using var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, encoding);

        writer.Write(content);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }
}
