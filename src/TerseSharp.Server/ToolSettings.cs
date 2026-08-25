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
    public const string FileName = ".terse.json";

    public static async Task<ToolOverrides> LoadAsync(string directory, CancellationToken cancellationToken)
    {
        if (Find(directory) is not { } path)
            return ToolOverrides.None;

        try
        {
            var file = new FileInfo(path);

            return file.Length > MaxBytes
                ? ToolOverrides.None with { Path = path, Failure = Oversized(file.Length) }
                : Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), path);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ToolOverrides.None with { Path = path, Failure = exception.Message };
        }
    }

    public static string? Find(string directory)
    {
        var current = Directory.Exists(directory) ? new DirectoryInfo(directory) : null;

        while (current is not null)
        {
            var candidate = System.IO.Path.Combine(current.FullName, FileName);

            if (File.Exists(candidate))
                return candidate;

            current = AtRepositoryRoot(current) ? null : current.Parent;
        }

        return null;
    }

    private static bool AtRepositoryRoot(DirectoryInfo directory) =>
        Directory.Exists(System.IO.Path.Combine(directory.FullName, ".git"))
            || File.Exists(System.IO.Path.Combine(directory.FullName, ".git"));

    public static ToolOverrides Parse(string json, string? path)
    {
        try
        {
            return Read(JsonNode.Parse(json) as JsonObject, path);
        }
        catch (JsonException exception)
        {
            return ToolOverrides.None with { Path = path, Failure = exception.Message };
        }
    }

    public static string? Notice(ToolOverrides overrides) => overrides switch
    {
        { Failure: { } failure } => string.Create(
            CultureInfo.InvariantCulture,
            $"terse: {overrides.Path} could not be read - {failure}; advertising every tool"),
        { Ignored: [_, ..] ignored } => string.Create(
            CultureInfo.InvariantCulture,
            $"terse: {overrides.Path} ignored {string.Join(", ", ignored)} - each must be a true/false value under 'groups' ({ToolGroups.Names()}) or under 'names' (an advertised tool name)"),
        _ => null,
    };

    private static ToolOverrides Read(JsonObject? root, string? path)
    {
        if (root?["tools"] is not { } tools)
            return ToolOverrides.None with { Path = path };

        var rules = new ToolRules([], ImmutableArray.CreateBuilder<string>(), ImmutableArray.CreateBuilder<string>());

        if (tools is JsonObject requested)
            Sections(requested, rules);
        else
            rules.Ignored.Add("tools");

        return new(
            path,
            rules.Decisions.ToFrozenDictionary(StringComparer.Ordinal),
            rules.Off.ToImmutable(),
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

        if (!advertised)
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

    private const int MaxBytes = 64 * 1024;

    private static string Oversized(long length) => string.Create(
            CultureInfo.InvariantCulture,
            $"it is {length} bytes, past the {MaxBytes}-byte ceiling");

    private const string Groups = "groups";
    private const string Names = "names";
}
