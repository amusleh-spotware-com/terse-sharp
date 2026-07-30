using System.Text.Json;
using System.Text.Json.Nodes;

namespace TerseSharp.Server;

public static class ClientRegistrar
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static IReadOnlyList<ClientTarget> Known() =>
    [
        new("claude-code", Path.Combine(Home(), ".claude.json")),
        new("cursor", Path.Combine(Home(), ".cursor", "mcp.json")),
        new("vscode", Path.Combine(Home(), ".vscode", "mcp.json")),
        new("windsurf", Path.Combine(Home(), ".codeium", "windsurf", "mcp_config.json")),
    ];

    public static string Register(string? client, string? workspace)
    {
        var targets = Select(client);
        var lines = targets.Select(target => Apply(target, workspace)).ToArray();

        return string.Join("\n", lines);
    }

    public static string Unregister(string? client)
    {
        var lines = Select(client).Select(Remove).ToArray();

        return string.Join("\n", lines);
    }

    private static ClientTarget[] Select(string? client) =>
        string.IsNullOrWhiteSpace(client)
            ? [.. Known().Where(target => File.Exists(target.ConfigPath) || Directory.Exists(Path.GetDirectoryName(target.ConfigPath)))]
            : [.. Known().Where(target => target.Name.Equals(client, StringComparison.OrdinalIgnoreCase))];

    private static string Apply(ClientTarget target, string? workspace)
    {
        var root = Load(target.ConfigPath);
        var servers = Servers(root);

        servers["terse-sharp"] = Entry(workspace);
        Save(target.ConfigPath, root);

        return "registered " + target.Name + " -> " + target.ConfigPath;
    }

    private static string Remove(ClientTarget target)
    {
        if (!File.Exists(target.ConfigPath))
            return "skipped " + target.Name + " (no config)";

        var root = Load(target.ConfigPath);

        Servers(root).Remove("terse-sharp");
        Save(target.ConfigPath, root);

        return "removed from " + target.Name;
    }

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

    private static void Save(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temporary = path + ".terse.tmp";

        File.WriteAllText(temporary, root.ToJsonString(Indented));
        File.Move(temporary, path, overwrite: true);
    }

    private static string Home() =>
        Environment.GetEnvironmentVariable("TERSE_HOME") is { Length: > 0 } overridden
            ? overridden
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}

public sealed record ClientTarget(string Name, string ConfigPath);
