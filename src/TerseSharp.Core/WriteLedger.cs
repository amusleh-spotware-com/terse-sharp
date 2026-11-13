namespace TerseSharp.Core;

internal sealed class WriteLedger
{
    private static readonly AsyncLocal<ReadMark?> Current = new();

    private readonly Dictionary<string, Stamp> written = [with(PathBoundary.Comparer)];
    private long generation;

    public void MarkRead() => Current.Value = new ReadMark(this, Interlocked.Read(ref generation));

    public string[] Overtaken(IReadOnlyList<string> paths)
    {
        if (Mark() is not { } mark)
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

    public void Wrote(IEnumerable<string> paths)
    {
        var stamp = new Stamp(Interlocked.Increment(ref generation), Mark());

        foreach (var path in paths)
            written[path] = stamp;
    }

    public string[] TakeOvertaken()
    {
        if (Mark() is not { } mark)
            return [];

        var overtaken = mark.Overtaken;

        mark.Overtaken = [];

        return overtaken;
    }

    private ReadMark? Mark() =>
        Current.Value is { } mark && ReferenceEquals(mark.Ledger, this) ? mark : null;

    private readonly record struct Stamp(long Generation, ReadMark? Writer);

    private sealed class ReadMark(WriteLedger ledger, long generation)
    {
        public WriteLedger Ledger { get; } = ledger;

        public long Generation { get; } = generation;

        public string[] Overtaken { get; set; } = [];
    }
}
