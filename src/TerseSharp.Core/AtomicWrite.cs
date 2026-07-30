namespace TerseSharp.Core;

public static class AtomicWrite
{
    public static void Text(string path, string content)
    {
        var temporary = path + ".terse-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";

        try
        {
            Persist(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void Persist(string temporary, string content)
    {
        using var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);

        writer.Write(content);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }
}
