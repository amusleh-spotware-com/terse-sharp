using System.Text.Json.Nodes;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ClientRegistrarTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "terse-tests", Guid.NewGuid().ToString());

    public ClientRegistrarTests()
    {
        Directory.CreateDirectory(home);
        Environment.SetEnvironmentVariable("TERSE_HOME", home);
    }

    [Fact]
    public void Register_WritesTheServerEntryWithTheWorkspaceArgument()
    {
        File.WriteAllText(ClaudeConfig, "{}");

        ClientRegistrar.Register("claude-code", @"C:\repo\App.slnx");

        var servers = Load()["mcpServers"]!.AsObject();

        Assert.Equal("terse", servers["terse-sharp"]!["command"]!.GetValue<string>());
        Assert.Equal(
            ["serve", "--workspace", @"C:\repo\App.slnx"],
            servers["terse-sharp"]!["args"]!.AsArray().Select(node => node!.GetValue<string>()));
    }

    [Fact]
    public void Register_PreservesEveryOtherServer()
    {
        File.WriteAllText(ClaudeConfig, """{"mcpServers":{"other":{"command":"x"}},"unrelated":42}""");

        ClientRegistrar.Register("claude-code", null);

        var root = Load();

        Assert.Equal(42, root["unrelated"]!.GetValue<int>());
        Assert.Equal("x", root["mcpServers"]!["other"]!["command"]!.GetValue<string>());
        Assert.NotNull(root["mcpServers"]!["terse-sharp"]);
    }

    [Fact]
    public void Unregister_RemovesOnlyTheTerseEntry()
    {
        File.WriteAllText(ClaudeConfig, """{"mcpServers":{"other":{"command":"x"}}}""");

        ClientRegistrar.Register("claude-code", null);
        ClientRegistrar.Unregister("claude-code");

        var servers = Load()["mcpServers"]!.AsObject();

        Assert.Null(servers["terse-sharp"]);
        Assert.NotNull(servers["other"]);
    }

    [Fact]
    public void Register_WithoutAWorkspace_OmitsTheWorkspaceArgument()
    {
        File.WriteAllText(ClaudeConfig, "{}");

        ClientRegistrar.Register("claude-code", null);

        Assert.Equal(
            ["serve"],
            Load()["mcpServers"]!["terse-sharp"]!["args"]!.AsArray().Select(node => node!.GetValue<string>()));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TERSE_HOME", null);
        Directory.Delete(home, recursive: true);
    }

    private string ClaudeConfig => Path.Combine(home, ".claude.json");

    private JsonObject Load() => JsonNode.Parse(File.ReadAllText(ClaudeConfig))!.AsObject();
}
