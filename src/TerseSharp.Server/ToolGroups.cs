using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
using ModelContextProtocol.Server;

namespace TerseSharp.Server;

public static class ToolGroups
{
    private const string Suffix = "Tools";

    public static FrozenDictionary<string, ImmutableArray<string>> All { get; } = Discover();

    public static FrozenSet<string> Tools { get; } =
            All.Values.SelectMany(names => names).ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static string? Named(string tool) => Tools.TryGetValue(tool, out var advertised) ? advertised : null;

    public static string Names() => string.Join(", ", All.Keys.Order(StringComparer.Ordinal));

    public static string? Of(string tool)
    {
        foreach (var group in All)
        {
            if (group.Value.Contains(tool, StringComparer.Ordinal))
                return group.Key;
        }

        return null;
    }

    private static FrozenDictionary<string, ImmutableArray<string>> Discover()
    {
        var groups = new Dictionary<string, ImmutableArray<string>.Builder>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in typeof(ToolGroups).Assembly.GetTypes().Where(Declares))
            Collect(groups, type);

        return groups.ToFrozenDictionary(
            group => group.Key,
            group => group.Value.ToImmutable(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool Declares(Type type) =>
        type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null;

    private static void Collect(Dictionary<string, ImmutableArray<string>.Builder> groups, Type type)
    {
        var group = Group(type.Name);

        if (!groups.TryGetValue(group, out var names))
            groups[group] = names = ImmutableArray.CreateBuilder<string>();

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (method.GetCustomAttribute<McpServerToolAttribute>()?.Name is { Length: > 0 } advertised)
                names.Add(advertised);
        }
    }

    private static string Group(ReadOnlySpan<char> type) =>
        (type.EndsWith(Suffix, StringComparison.Ordinal) ? type[..^Suffix.Length] : type).ToString().ToLowerInvariant();
}
