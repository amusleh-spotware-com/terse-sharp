using System.Text.Json;
using System.Text.Json.Nodes;

namespace TerseSharp.Server;

public static class ClientRegistrar
{
    private const string ServerName = "terse-sharp";
    private const string ClaudeCode = "claude-code";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static IReadOnlyList<ClientTarget> Known() =>
    [
        new(ClaudeCode, Path.Combine(ClaudeConfigDirectory() ?? Home(), ".claude.json")),
        new("cursor", Path.Combine(Home(), ".cursor", "mcp.json")),
        new("vscode", Path.Combine(Home(), ".vscode", "mcp.json")),
        new("windsurf", Path.Combine(Home(), ".codeium", "windsurf", "mcp_config.json")),
    ];

    public static async Task<string> Register(string? client, string? workspace)
    {
        var lines = new List<string>();

        foreach (var target in Select(client))
            lines.Add(await Apply(target, workspace).ConfigureAwait(false));

        return Joined([.. lines]);
    }

    public static async Task<string> InstallSkill()
    {
        var target = SkillPath();

        await AtomicWrite.TextAsync(target, SkillAsset.Read()).ConfigureAwait(false);

        return "installed skill -> " + target;
    }

    public static async Task<string> InstallGuard()
    {
        var target = SettingsPath();

        await SaveAsync(target, Guarded(Settings(target))).ConfigureAwait(false);

        return "installed guard -> " + target;
    }

    private static JsonArray GuardMatchers(JsonArray? existing)
    {
        var kept = existing?.Select(Without).OfType<JsonNode>() ?? [];

        return [.. kept, GuardEntry()];
    }

    private static JsonNode? Without(JsonNode? entry)
    {
        if (entry is not JsonObject matcher || matcher["hooks"] is not JsonArray hooks)
            return entry?.DeepClone();

        var others = hooks.Where(hook => !IsGuard(hook)).Select(hook => hook!.DeepClone()).ToArray();

        if (others.Length == hooks.Count)
            return matcher.DeepClone();

        if (others.Length is 0)
            return null;

        var clone = matcher.DeepClone();

        clone["hooks"] = new JsonArray(others);

        return clone;
    }

    private static bool IsGuard(JsonNode? hook) =>
        hook is JsonObject declared
        && declared["command"]?.GetValue<string>()?.Contains("terse guard", StringComparison.Ordinal) is true;

    private static JsonObject GuardEntry() => new()
    {
        ["matcher"] = "Read|Write|Edit|MultiEdit|NotebookEdit|Grep|Glob|Bash",
        ["hooks"] = new JsonArray(new JsonObject
        {
            ["type"] = "command",
            ["command"] = "terse guard",
        }),
    };

    private static JsonObject Hooks(JsonObject root)
    {
        if (root["hooks"] is not JsonObject hooks)
            root["hooks"] = hooks = [];

        return hooks;
    }

    public static async Task<string> Unregister(string? client)
    {
        var lines = new List<string>();

        foreach (var target in Select(client))
            lines.Add(await Remove(target).ConfigureAwait(false));

        return Joined([.. lines]);
    }

    public static ClientConfigState State(ClientTarget target)
    {
        if (!File.Exists(target.ConfigPath))
            return ClientConfigState.NotFound;

        return Parse(target.ConfigPath) switch
        {
            null => ClientConfigState.Invalid,
            var root when root["mcpServers"] is JsonObject servers && servers.ContainsKey(ServerName) => ClientConfigState.Registered,
            _ => ClientConfigState.NotRegistered,
        };
    }

    private static ClientTarget[] Select(string? client) =>
        string.IsNullOrWhiteSpace(client)
            ? [.. Known().Where(Detected)]
            : [.. Known().Where(target => target.Name.Equals(client, StringComparison.OrdinalIgnoreCase))];

    private static bool Detected(ClientTarget target) =>
        (target.Name is ClaudeCode && ClaudeConfigDirectory() is not null)
        || File.Exists(target.ConfigPath)
        || Directory.Exists(Path.GetDirectoryName(target.ConfigPath));

    private static string Joined(string[] lines) =>
        lines.Length is 0 ? "no MCP clients matched" : string.Join("\n", lines);

    private static async Task<string> Apply(ClientTarget target, string? workspace)
    {
        if (State(target) is ClientConfigState.Invalid)
            return Skipped(target, "not valid JSON");

        var root = Load(target.ConfigPath);
        var servers = Servers(root);

        servers[ServerName] = Entry(workspace);

        await SaveAsync(target.ConfigPath, root).ConfigureAwait(false);

        return "registered " + target.Name + " -> " + target.ConfigPath;
    }
    private static async Task<string> Remove(ClientTarget target)
    {
        if (State(target) is ClientConfigState.Invalid)
            return Skipped(target, "not valid JSON");

        if (!File.Exists(target.ConfigPath))
            return Skipped(target, "no config");

        var root = Load(target.ConfigPath);

        if (root["mcpServers"] is not JsonObject servers || !servers.Remove(ServerName))
            return Skipped(target, "not registered");

        await SaveAsync(target.ConfigPath, root).ConfigureAwait(false);

        return "removed from " + target.Name;
    }
    private static string Skipped(ClientTarget target, string reason) =>
        "skipped " + target.Name + " (" + reason + ": " + target.ConfigPath + ")";

    private static JsonObject Entry(string? workspace)
    {
        var arguments = new JsonArray("serve");

        if (!string.IsNullOrWhiteSpace(workspace))
        {
            arguments.Add("--workspace");
            arguments.Add(workspace);
        }

        return new JsonObject { ["command"] = "terse", ["args"] = arguments };
    }

    private static JsonObject Servers(JsonObject root)
    {
        if (root["mcpServers"] is JsonObject existing)
            return existing;

        var created = new JsonObject();

        root["mcpServers"] = created;

        return created;
    }

    private static JsonObject Load(string path) =>
        File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject parsed ? parsed : [];

    private static JsonObject? Parse(string path)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task SaveAsync(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await AtomicWrite.TextAsync(path, root.ToJsonString(Indented)).ConfigureAwait(false);
    }

    internal static string Home() =>
        Environment.GetEnvironmentVariable("TERSE_HOME") is { Length: > 0 } overridden
            ? overridden
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string ClaudeSkillsDirectory() =>
        Path.Combine(ClaudeConfigDirectory() ?? Path.Combine(Home(), ".claude"), "skills");

    private static string? ClaudeConfigDirectory() =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")?.Trim() is { Length: > 0 } directory
            ? Path.GetFullPath(directory)
            : null;

    private static string SkillPath() => Path.Combine(ClaudeSkillsDirectory(), ServerName, "SKILL.md");

    private static string SettingsPath() =>
        Path.Combine(ClaudeConfigDirectory() ?? Path.Combine(Home(), ".claude"), "settings.json");

    private static JsonObject Guarded(JsonObject root)
    {
        var updated = (JsonObject)root.DeepClone();

        Hooks(updated)["PreToolUse"] = GuardMatchers(Hooks(updated)["PreToolUse"] as JsonArray);

        return updated;
    }

    private static JsonObject Settings(string path) => (File.Exists(path) ? Parse(path) : null) ?? [];

    private static async Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool GuardPresent(JsonObject? root) =>
        root?["hooks"] is JsonObject hooks
        && hooks["PreToolUse"] is JsonArray matchers
        && matchers.OfType<JsonObject>().Any(entry => entry["hooks"] is JsonArray declared && declared.Any(IsGuard));

    private static bool GuardIsCurrent(JsonObject? root) =>
        root is not null
        && string.Equals(root.ToJsonString(Indented), Guarded(root).ToJsonString(Indented), StringComparison.Ordinal);

    private static async Task<bool> SkillIsCurrentAsync(string path, CancellationToken cancellationToken) =>
        string.Equals(await ReadAsync(path, cancellationToken).ConfigureAwait(false), SkillAsset.Read(), StringComparison.Ordinal);

    public static async Task<AssetState> AssetsAsync(CancellationToken cancellationToken)
    {
        var skill = SkillPath();
        var settings = SettingsPath();
        var root = File.Exists(settings) ? Parse(settings) : null;
        var installed = File.Exists(skill);

        return new AssetState(
            installed,
            installed && await SkillIsCurrentAsync(skill, cancellationToken).ConfigureAwait(false),
            GuardPresent(root),
            GuardIsCurrent(root));
    }

    private static async Task<string?> RewriteAsync(AssetState state)
    {
        var refreshed = new List<string>(2);

        if (state is { SkillInstalled: true, SkillCurrent: false })
            refreshed.Add(await InstallSkill().ConfigureAwait(false));

        if (state is { GuardInstalled: true, GuardCurrent: false })
            refreshed.Add(await InstallGuard().ConfigureAwait(false));

        return refreshed.Count is 0 ? null : string.Join("\n", refreshed);
    }

    public static async Task<string?> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await RewriteAsync(await AssetsAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }
}

public sealed record ClientTarget(string Name, string ConfigPath);

public enum ClientConfigState
{
    NotFound,
    Invalid,
    NotRegistered,
    Registered,
}
