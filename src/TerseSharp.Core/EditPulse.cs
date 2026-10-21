namespace TerseSharp.Core;

public static class EditPulse
{
    private const int MaxRemembered = 8;

    private static int changed;
    private static int material;

    private static readonly Lock Gate = new();

    private static readonly List<(int At, string Path)> Recent = new(MaxRemembered);

    public static int Changed => Volatile.Read(ref changed);

    public static int Material => Volatile.Read(ref material);

    public static void Bump(int documents)
    {
        if (documents > 0)
        {
            Interlocked.Add(ref changed, documents);
            Interlocked.Add(ref material, documents);
        }
    }

    public static void Bump(string path)
    {
        Interlocked.Increment(ref changed);

        if (ChangesABuild(path))
            Remember(path, Interlocked.Increment(ref material));
    }

    private static int observed;

    public static int Observed => Volatile.Read(ref observed);

    public static void Observe() => Interlocked.Increment(ref observed);

    private static void Remember(string path, int at)
    {
        lock (Gate)
        {
            if (Recent.Count == MaxRemembered)
                Recent.RemoveAt(0);

            Recent.Add((at, path));
        }
    }

    public static IReadOnlyList<string> Written(int watermark, int ceiling)
    {
        lock (Gate)
        {
            var written = new List<string>(Recent.Count);

            foreach (var entry in Recent)
            {
                if (entry.At > watermark && entry.At <= ceiling)
                    written.Add(entry.Path);
            }

            return written;
        }
    }

    private static readonly string[] NoteExtensions = [".md", ".markdown"];

    public static bool ChangesABuild(ReadOnlySpan<char> path)
    {
        var extension = Path.GetExtension(path);

        foreach (var note in NoteExtensions)
        {
            if (extension.Equals(note, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
