using System.Collections.Concurrent;
using System.Text;

namespace TerseSharp.Core;

public static class PolicyCache
{
    private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<PolicyOptions> ForAsync(string root, CancellationToken cancellationToken)
    {
        var chain = TerseConfigFile.Chain(root);
        var key = Key(chain);

        if (Entries.TryGetValue(root, out var cached) && cached.Matches(key))
            return cached.Options;

        var options = await PolicySettings.LoadAsync(chain, cancellationToken).ConfigureAwait(false);

        Entries[root] = new Entry(key, options);

        return options;
    }

    public static void Forget() => Entries.Clear();

    private static string Key(IReadOnlyList<string> chain)
    {
        var builder = new StringBuilder();

        foreach (var path in chain)
        {
            var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0L;

            builder.Append(path).Append('|').Append(stamp.ToString(CultureInfo.InvariantCulture)).Append(';');
        }

        return builder.ToString();
    }

    private sealed record Entry(string Key, PolicyOptions Options)
    {
        public bool Matches(string key) => string.Equals(Key, key, StringComparison.Ordinal);
    }
}
