namespace TerseSharp.Core;

public readonly record struct IndexKey(int Generation, bool Trusted);

public sealed class WorkspaceIndexes(string root, WorkspaceSync sync) : IDisposable
{
    private readonly IndexSlot<XamlResourceGraph> xaml = new();
    private readonly IndexSlot<ResxIndex> resx = new();
    private readonly IndexSlot<RegistrationIndex> registrations = new();

    public ParsedDocumentCache Documents { get; } = new();

    public XamlResourceGraph Xaml() =>
        xaml.Get(Key(ChangeKind.Xaml), previous => XamlResourceGraph.Build(root, Documents, previous));

    public ResxIndex Resx() => resx.Get(Key(ChangeKind.Resx), _ => ResxIndex.Of(root, Documents));

    public Task<RegistrationIndex> RegistrationsAsync(
        LoadedWorkspace workspace,
        CancellationToken cancellationToken) =>
        registrations.GetAsync(
            Key(ChangeKind.Code),
            (_, token) => RegistrationIndex.OfAsync(workspace, token),
            cancellationToken);

    public Result<XamlDocument> Markup(string fullPath) =>
        Documents.Load(fullPath, ParsedDocumentCache.MarkupFactor, XamlDocument.Load);

    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"index=xaml({Counters(xaml)} files={Size(xaml.Current?.FileCount)}) resx({Counters(resx)} families={Size(resx.Current?.Families.Count)}) code({Counters(registrations)} calls={Size(registrations.Current?.Count)}) documents={Documents.Count}/{ParsedDocumentCache.MaxDocuments} parses={Documents.Parses}");

    public void Dispose()
    {
        xaml.Dispose();
        resx.Dispose();
        registrations.Dispose();
    }

    private IndexKey Key(ChangeKind kind) => new(sync.Generation(kind), Trusted);

    private bool Trusted =>
        sync.State is WatchState.Active && !sync.Reloading && sync.PendingCount is 0;

    private static string Counters<TIndex>(IndexSlot<TIndex> slot)
        where TIndex : class =>
        string.Create(CultureInfo.InvariantCulture, $"hit={slot.Hits} miss={slot.Misses}");

    private static string Size(int? count) =>
        count is { } known ? known.ToString(CultureInfo.InvariantCulture) : "-";
}

internal sealed class IndexSlot<TIndex> : IDisposable
    where TIndex : class
{
    private readonly SemaphoreSlim gate = new(1, 1);

    private Published? published;
    private int hits;
    private int misses;

    public int Hits => Volatile.Read(ref hits);

    public int Misses => Volatile.Read(ref misses);

    public TIndex? Current => Volatile.Read(ref published)?.Index;

    public TIndex Get(IndexKey key, Func<TIndex?, TIndex> build)
    {
        var before = Volatile.Read(ref published);

        if (Serves(before, key))
            return Hit(before!);

        gate.Wait();

        try
        {
            var after = Volatile.Read(ref published);

            return Shared(before, after, key) ?? Publish(build(after?.Index), key);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TIndex> GetAsync(
        IndexKey key,
        Func<TIndex?, CancellationToken, Task<TIndex>> build,
        CancellationToken cancellationToken)
    {
        var before = Volatile.Read(ref published);

        if (Serves(before, key))
            return Hit(before!);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var after = Volatile.Read(ref published);

            return Shared(before, after, key)
                ?? Publish(await build(after?.Index, cancellationToken).ConfigureAwait(false), key);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();

    private TIndex Hit(Published entry)
    {
        Interlocked.Increment(ref hits);

        return entry.Index;
    }

    private TIndex? Shared(Published? before, Published? after, IndexKey key) =>
        after is not null && (key.Trusted ? Serves(after, key) : !ReferenceEquals(after, before))
            ? Hit(after)
            : null;

    private TIndex Publish(TIndex index, IndexKey key)
    {
        Interlocked.Increment(ref misses);
        Volatile.Write(ref published, new Published(index, key.Generation));

        return index;
    }

    private static bool Serves(Published? entry, IndexKey key) =>
        key.Trusted && entry is not null && entry.Generation == key.Generation;

    private sealed record Published(TIndex Index, int Generation);
}
