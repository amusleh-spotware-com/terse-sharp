using System.Text.Json;
using System.Text.Json.Nodes;

namespace TerseSharp.Core;

public static class WorkspaceSetting
{
    public static async Task<string?> DefaultAsync(string directory, CancellationToken cancellationToken) =>
        await ConfiguredAsync(await TerseConfigFile.ChainAsync(directory, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false)
            ?? (WorkspaceDiscovery.Find(directory) is [var first, ..] ? first : null);

    public static string? Configured(string json, string directory, string host)
    {
        try
        {
            return JsonNode.Parse(json) is JsonObject { } root && root["workspace"] is JsonObject workspace
                ? Existing(directory, Named(workspace["path"], host))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> ConfiguredAsync(IReadOnlyList<string> chain, CancellationToken cancellationToken)
    {
        string? configured = null;

        foreach (var path in chain)
            configured = await ReadAsync(path, cancellationToken).ConfigureAwait(false) ?? configured;

        return configured;
    }

    private static string? Named(JsonNode? node, string host) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var path) => path,
        JsonObject perHost when perHost[host] is JsonValue value && value.TryGetValue<string>(out var path) => path,
        _ => null,
    };

    private static string? Existing(string directory, string? path) =>
        path is { Length: > 0 } && Path.GetFullPath(path, directory) is var full && File.Exists(full) ? full : null;

    private static async Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length <= TerseConfigFile.MaxBytes
                ? Configured(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), Path.GetDirectoryName(path) ?? path, HostSystem.Name)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
