namespace TerseSharp.Core;

internal sealed class EvictionMemory
{
    private const int Capacity = 16;

    private readonly Dictionary<string, WorkspaceEviction> evictions = [with(StringComparer.OrdinalIgnoreCase)];

    private readonly Lock gate = new();

    public void Remember(string evicted, string by, int limit, DateTimeOffset atUtc)
    {
        lock (gate)
        {
            if (evictions.Count >= Capacity && !evictions.ContainsKey(evicted))
                evictions.Remove(evictions.MinBy(entry => entry.Value.EvictedUtc).Key);

            evictions[evicted] = new WorkspaceEviction(atUtc, by, limit);
        }
    }

    public WorkspaceEviction? Recalled(string full)
    {
        lock (gate)
            return evictions.TryGetValue(full, out var eviction) ? eviction : null;
    }

    public void Forget(string full)
    {
        lock (gate)
            evictions.Remove(full);
    }
}
