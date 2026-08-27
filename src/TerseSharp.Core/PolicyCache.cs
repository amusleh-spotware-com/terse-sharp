using System.Collections.Concurrent;

namespace TerseSharp.Core;

public static class PolicyCache
{
    private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<PolicyOptions> ForAsync(string root, CancellationToken cancellationToken)
    {
        var path = TerseConfigFile.Find(root);
        var stamp = Stamp(path);

        if (Entries.TryGetValue(root, out var cached) && cached.Matches(path, stamp))
            return cached.Options;

        var options = await PolicySettings.LoadAsync(root, cancellationToken).ConfigureAwait(false);

        Entries[root] = new Entry(path, stamp, options);

        return options;
    }

    public static void Forget() => Entries.Clear();

    private static DateTime Stamp(string? path) => path is null || !File.Exists(path)
        ? default
        : File.GetLastWriteTimeUtc(path);

    private sealed record Entry(string? Path, DateTime Stamp, PolicyOptions Options)
    {
        public bool Matches(string? path, DateTime stamp) =>
            string.Equals(Path, path, StringComparison.OrdinalIgnoreCase) && Stamp == stamp;
    }
}
