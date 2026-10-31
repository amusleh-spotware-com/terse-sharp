namespace TerseSharp.Core;

internal sealed class WriteLedger
{
    private static readonly AsyncLocal<ReadMark?> Current = new();

    private readonly Dictionary<string, Stamp> written = new(PathBoundary.Comparer);
    private long generation;

    public void MarkRead(LoadedWorkspace owner) => Current.Value = new ReadMark(owner, Interlocked.Read(ref generation));

    public string[] Overtaken(LoadedWorkspace owner, IReadOnlyList<string> paths)
    {
        if (Mark(owner) is not { } mark)
            return [];

        var overtaken = new List<string>(paths.Count);

        foreach (var path in paths)
        {
            if (written.TryGetValue(path, out var stamp) && stamp.Generation > mark.Generation && !ReferenceEquals(stamp.Writer, mark))
                overtaken.Add(path);
        }

        mark.Overtaken = [.. overtaken];

        return mark.Overtaken;
    }

    public void Wrote(LoadedWorkspace owner, IEnumerable<string> paths)
    {
        var stamp = new Stamp(Interlocked.Increment(ref generation), Mark(owner));

        foreach (var path in paths)
            written[path] = stamp;
    }

    public static string[] TakeOvertaken(LoadedWorkspace owner)
    {
        if (Mark(owner) is not { } mark)
            return [];

        var overtaken = mark.Overtaken;

        mark.Overtaken = [];

        return overtaken;
    }

    private static ReadMark? Mark(LoadedWorkspace owner) =>
        Current.Value is { } mark && ReferenceEquals(mark.Owner, owner) ? mark : null;

    private readonly record struct Stamp(long Generation, ReadMark? Writer);

    private sealed class ReadMark(LoadedWorkspace owner, long generation)
    {
        public LoadedWorkspace Owner { get; } = owner;

        public long Generation { get; } = generation;

        public string[] Overtaken { get; set; } = [];
    }
}
