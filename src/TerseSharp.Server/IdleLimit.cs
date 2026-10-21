using System.Text.Json;
using System.Text.Json.Nodes;

namespace TerseSharp.Server;

public static class IdleLimit
{
    public const int DefaultMinutes = 60;

    public const string Variable = "TERSE_IDLE_MINUTES";

    public const string Setting = ".terse.json workspace.idleMinutes";

    public static async Task<TimeSpan> ResolveAsync(int? option, string directory, CancellationToken cancellationToken) =>
        Resolve(option, Environment.GetEnvironmentVariable(Variable), await ConfiguredAsync(TerseConfigFile.Chain(directory), cancellationToken).ConfigureAwait(false));

    public static TimeSpan Resolve(int? option, string? environment, int? configured = null) =>
        Minutes(option ?? Parse(environment) ?? configured ?? DefaultMinutes);

    public static async Task<int?> ConfiguredAsync(IReadOnlyList<string> chain, CancellationToken cancellationToken)
    {
        int? configured = null;

        foreach (var path in chain)
            configured = await ReadAsync(path, cancellationToken).ConfigureAwait(false) ?? configured;

        return configured;
    }

    public static int? Configured(string json)
    {
        try
        {
            return JsonNode.Parse(json) is JsonObject { } root && root["workspace"] is JsonObject workspace
                ? IdleMinutes(workspace["idleMinutes"])
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? IdleMinutes(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var minutes) && minutes >= 0 ? minutes : null;

    private static async Task<int?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length <= TerseConfigFile.MaxBytes
                ? Configured(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false))
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static TimeSpan Minutes(int minutes) =>
        minutes > 0 ? TimeSpan.FromMinutes(minutes) : TimeSpan.Zero;

    private static int? Parse(string? environment) =>
        int.TryParse(environment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;
}
