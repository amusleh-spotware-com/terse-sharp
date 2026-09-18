namespace TerseSharp.Core;

public static class EditPulse
{
    private static int changed;
    private static int material;

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
            Interlocked.Increment(ref material);
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
