namespace TerseSharp.Core;

public sealed class ParsedDocumentCache(int maxDocuments = ParsedDocumentCache.MaxDocuments, long maxBytes = ParsedDocumentCache.MaxBytes)
{
    public const int MaxDocuments = 128;

    public const long MaxBytes = 32L * 1024 * 1024;

    public const int MarkupFactor = 8;

    public const int ResourceFactor = 3;

    private readonly Lock gate = new();
    private readonly Dictionary<string, LinkedListNode<Cached>> nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<Cached> order = new();

    private long bytes;
    private int parses;

    public int Count
    {
        get
        {
            lock (gate)
                return nodes.Count;
        }
    }

    public long Bytes
    {
        get
        {
            lock (gate)
                return bytes;
        }
    }

    public int Parses => Volatile.Read(ref parses);

    public Result<TDocument> Load<TDocument>(string path, int factor, Func<string, Result<TDocument>> parse)
        where TDocument : class =>
        Load(path, FileStamp.Of(path), factor, parse);

    public Result<TDocument> Load<TDocument>(
        string path,
        FileStamp stamp,
        int factor,
        Func<string, Result<TDocument>> parse)
        where TDocument : class
    {
        if (stamp == FileStamp.Missing)
            return Result.Fail<TDocument>(Errors.DocumentNotFound(path));

        if (Take<TDocument>(path, stamp) is { } cached)
            return Result.Ok(cached);

        Interlocked.Increment(ref parses);

        var parsed = parse(path);
        var parsedAt = FileStamp.Of(path);

        if (parsed.IsOk && parsedAt == stamp)
            Store(path, stamp, parsed.Value!, (long)stamp.Length * factor);

        return parsed;
    }

    public void Forget(string path)
    {
        lock (gate)
        {
            if (nodes.TryGetValue(path, out var node))
                Drop(node);
        }
    }

    private TDocument? Take<TDocument>(string path, FileStamp stamp)
        where TDocument : class
    {
        lock (gate)
        {
            if (!nodes.TryGetValue(path, out var node))
                return null;

            if (node.Value.Stamp != stamp || node.Value.Document is not TDocument document)
            {
                Drop(node);

                return null;
            }

            order.Remove(node);
            order.AddFirst(node);

            return document;
        }
    }

    private void Store(string path, FileStamp stamp, object document, long weight)
    {
        lock (gate)
        {
            if (nodes.TryGetValue(path, out var existing))
                Drop(existing);

            nodes[path] = order.AddFirst(new Cached(path, stamp, document, weight));
            bytes += weight;

            Evict();
        }
    }

    private void Evict()
    {
        while ((nodes.Count > maxDocuments || bytes > maxBytes) && order.Last is { } oldest)
            Drop(oldest);
    }

    private void Drop(LinkedListNode<Cached> node)
    {
        bytes -= node.Value.Weight;
        nodes.Remove(node.Value.Path);
        order.Remove(node);
    }

    private sealed record Cached(string Path, FileStamp Stamp, object Document, long Weight);
}
