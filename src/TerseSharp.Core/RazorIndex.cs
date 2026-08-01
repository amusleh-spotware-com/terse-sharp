namespace TerseSharp.Core;

public sealed class RazorIndex
{
    private readonly Dictionary<string, Entry> entries;

    private RazorIndex(Dictionary<string, Entry> entries)
    {
        this.entries = entries;
        Documents = [.. entries.Values.Select(entry => entry.Document)];
    }

    public IReadOnlyList<RazorDocument> Documents { get; }

    public int FileCount => entries.Count;

    public static RazorIndex Build(string root, RazorIndex? previous)
    {
        var files = RazorFiles.Enumerate(root).ToArray();
        var parsed = new Entry?[files.Length];

        Parallel.For(0, files.Length, index => parsed[index] = Read(files[index], previous));

        return new RazorIndex(Collect(files, parsed));
    }

    public RazorDocument? Of(string fullPath) =>
        entries.TryGetValue(fullPath, out var entry) ? entry.Document : Read(fullPath, null)?.Document;

    private static Dictionary<string, Entry> Collect(string[] files, Entry?[] parsed)
    {
        var entries = new Dictionary<string, Entry>(files.Length, StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < files.Length; index++)
        {
            if (parsed[index] is { } entry)
                entries[files[index]] = entry;
        }

        return entries;
    }

    private static Entry? Read(string fullPath, RazorIndex? previous)
    {
        try
        {
            var info = new FileInfo(fullPath);

            if (previous is not null && previous.entries.TryGetValue(fullPath, out var cached) && cached.Matches(info))
                return cached;

            var document = RazorDocument.Load(fullPath);

            return document.IsOk ? new Entry(info.LastWriteTimeUtc, info.Length, document.Value!) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private readonly record struct Entry(DateTime WriteUtc, long Length, RazorDocument Document)
    {
        public bool Matches(FileInfo info) => info.LastWriteTimeUtc == WriteUtc && info.Length == Length;
    }
}
