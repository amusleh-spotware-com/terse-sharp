using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TerseSharp.Server;

public sealed record ToolOverrides(
    string? Path,
    FrozenDictionary<string, bool> Tools,
    ImmutableArray<string> Off,
    ImmutableArray<string> Ignored,
    string? Failure)
{
    public static ToolOverrides None { get; } = new(null, FrozenDictionary<string, bool>.Empty, [], [], null);

    public bool Configured => Tools.Count is not 0 || !Ignored.IsEmpty || Failure is not null;

    public int Hidden => Tools.Count(decision => !decision.Value);

    public bool? Decision(string tool) => Tools.TryGetValue(tool, out var advertised) ? advertised : null;
}

public static class ToolSettings
{
    public const string FileName = TerseConfigFile.FileName;

    public static async Task<ToolOverrides> LoadAsync(string directory, CancellationToken cancellationToken)
    {
        var overrides = ToolOverrides.None;

        foreach (var path in TerseConfigFile.Chain(directory))
        {
            overrides = await ApplyAsync(overrides, path, cancellationToken).ConfigureAwait(false);

            if (overrides.Failure is not null)
                return overrides;
        }

        return overrides;
    }

    public static ToolOverrides Parse(string json, string? path) => Parse(json, path, ToolOverrides.None);

    public static string? Notice(ToolOverrides overrides) => overrides switch
    {
        { Failure: { } failure } => string.Create(
            CultureInfo.InvariantCulture,
            $"terse: {overrides.Path} could not be read - {failure}; {Surviving(overrides)}"),
        { Ignored: [_, ..] ignored } => string.Create(
            CultureInfo.InvariantCulture,
            $"terse: ignored {string.Join(", ", ignored)} - each must be a true/false value under 'groups' ({ToolGroups.Names()}) or under 'names' (an advertised tool name)"),
        _ => null,
    };

    private static ToolOverrides Read(JsonObject? root, string? path, ToolOverrides seed)
    {
        if (root?["tools"] is not { } tools)
            return seed;

        var rules = new ToolRules(
            new Dictionary<string, bool>(seed.Tools, StringComparer.Ordinal),
            ImmutableArray.CreateBuilder<string>(),
            ImmutableArray.CreateBuilder<string>());

        rules.Off.AddRange(seed.Off);
        rules.Ignored.AddRange(seed.Ignored);

        if (tools is JsonObject requested)
            Sections(requested, rules);
        else
            rules.Ignored.Add("tools");

        return new(
            path,
            rules.Decisions.ToFrozenDictionary(StringComparer.Ordinal),
            Hidden(rules),
            rules.Ignored.ToImmutable(),
            null);
    }

    private static void Sections(JsonObject requested, ToolRules rules)
    {
        Apply(requested, Groups, Expand, rules);
        Apply(requested, Names, Single, rules);

        foreach (var entry in requested)
        {
            if (entry.Key is not (Groups or Names))
                rules.Ignored.Add(entry.Key);
        }
    }

    private static void Apply(JsonObject requested, string section, Func<string, ImmutableArray<string>> resolve, ToolRules rules)
    {
        if (!requested.TryGetPropertyValue(section, out var declared))
            return;

        if (declared is not JsonObject entries)
        {
            rules.Ignored.Add(section);

            return;
        }

        foreach (var entry in entries)
            Route(entry, resolve(entry.Key), rules);
    }

    private static void Route(KeyValuePair<string, JsonNode?> entry, ImmutableArray<string> tools, ToolRules rules)
    {
        if (tools.IsEmpty || entry.Value is not JsonValue value || !value.TryGetValue<bool>(out var advertised))
        {
            rules.Ignored.Add(entry.Key);

            return;
        }

        foreach (var tool in tools)
            rules.Decisions[tool] = advertised;

        if (!advertised && !rules.Off.Contains(entry.Key, StringComparer.OrdinalIgnoreCase))
            rules.Off.Add(entry.Key);
    }

    private static ImmutableArray<string> Expand(string group) =>
        ToolGroups.All.TryGetValue(group, out var tools) ? tools : [];

    private static ImmutableArray<string> Single(string tool) =>
            ToolGroups.Named(tool) is { } advertised ? [advertised] : [];

    private sealed record ToolRules(
        Dictionary<string, bool> Decisions,
        ImmutableArray<string>.Builder Off,
        ImmutableArray<string>.Builder Ignored);

    private const string Groups = "groups";
    private const string Names = "names";

    public static ToolOverrides Parse(string json, string? path, ToolOverrides seed)
    {
        try
        {
            return Read(JsonNode.Parse(json) as JsonObject, path, seed);
        }
        catch (JsonException exception)
        {
            return ToolOverrides.None with { Path = path, Failure = exception.Message };
        }
    }

    private static async Task<ToolOverrides> ApplyAsync(ToolOverrides seed, string path, CancellationToken cancellationToken)
    {
        try
        {
            var file = new FileInfo(path);

            if (file.Length > TerseConfigFile.MaxBytes)
                return seed with { Path = path, Failure = TerseConfigFile.Oversized(file.Length) };

            var parsed = Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), path, seed);

            if (parsed.Failure is { } failure)
                return seed with { Path = path, Failure = failure };

            return ReferenceEquals(parsed, seed) ? seed : parsed with { Ignored = Qualified(seed, parsed, path) };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return seed with { Path = path, Failure = exception.Message };
        }
    }

    private static ImmutableArray<string> Hidden(ToolRules rules) =>
        [.. rules.Off.Where(key => Named(key).Any(tool => rules.Decisions.TryGetValue(tool, out var advertised) && !advertised))];

    private static ImmutableArray<string> Named(string key) =>
        Expand(key) is [_, ..] group ? group : Single(key);

    private static ImmutableArray<string> Qualified(ToolOverrides seed, ToolOverrides parsed, string path) =>
        [.. seed.Ignored, .. parsed.Ignored.Skip(seed.Ignored.Length).Select(key => path + ": " + key)];

    internal static string Surviving(ToolOverrides overrides) =>
        overrides.Hidden is 0 ? "it narrows nothing" : "the narrowing already in force still applies";
}
