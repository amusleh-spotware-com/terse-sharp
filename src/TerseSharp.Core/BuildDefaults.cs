using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TerseSharp.Core;

public sealed record BuildDefaults(string? Configuration, FrozenDictionary<string, string> Projects, string? Source)
{
    public static BuildDefaults None { get; } = new(null, FrozenDictionary<string, string>.Empty, null);

    public static async Task<BuildDefaults> LoadAsync(string directory, CancellationToken cancellationToken)
    {
        var defaults = None;

        foreach (var path in TerseConfigFile.Chain(directory))
            defaults = await MergedAsync(defaults, path, cancellationToken).ConfigureAwait(false);

        return defaults;
    }

    public static BuildDefaults Parse(string json, string path, BuildDefaults seed)
    {
        try
        {
            return JsonNode.Parse(json) is JsonObject { } root && root["build"] is JsonObject build ? Read(build, path, seed) : seed;
        }
        catch (JsonException)
        {
            return seed;
        }
    }

    public string? For(IReadOnlyList<string?> projects)
    {
        string? chosen = null;

        foreach (var project in projects)
        {
            var configured = Configured(project);

            if (configured is null || (chosen is not null && !string.Equals(chosen, configured, StringComparison.OrdinalIgnoreCase)))
                return Configuration;

            chosen = configured;
        }

        return chosen ?? Configuration;
    }

    private string? Configured(string? project) =>
        project is { Length: > 0 } && Projects.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(ProjectName(project), out var configured)
            ? configured
            : null;

    private static ReadOnlySpan<char> ProjectName(string project)
    {
        var name = Path.GetFileName(project.AsSpan());

        return name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(name)
                : name;
    }

    private static async Task<BuildDefaults> MergedAsync(BuildDefaults seed, string path, CancellationToken cancellationToken)
    {
        try
        {
            return new FileInfo(path).Length > TerseConfigFile.MaxBytes
                ? seed
                : Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), path, seed);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return seed;
        }
    }

    private static BuildDefaults Read(JsonObject build, string path, BuildDefaults seed)
    {
        var projects = new Dictionary<string, string>(seed.Projects, StringComparer.OrdinalIgnoreCase);

        if (build["projects"] is JsonObject named)
        {
            foreach (var (name, value) in named)
            {
                if (Text(value) is { } configuration)
                    projects[name] = configuration;
            }
        }

        return new BuildDefaults(Text(build["configuration"]) ?? seed.Configuration, projects.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase), path);
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;
}
