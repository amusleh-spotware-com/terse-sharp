using System.Text.Json.Nodes;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ClientRegistrarTests : IDisposable
{
    private const string Malformed = "{ // a comment\n}";

    private readonly string home = Path.Combine(Path.GetTempPath(), "terse-tests", Guid.NewGuid().ToString());
    private readonly string? previousConfigDirectory = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");

    public ClientRegistrarTests()
    {
        Directory.CreateDirectory(home);
        Environment.SetEnvironmentVariable("TERSE_HOME", home);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
    }

    [Fact]
    public async Task Register_WritesTheServerEntryWithTheWorkspaceArgument()
    {
        File.WriteAllText(ClaudeConfig, "{}");

        await ClientRegistrar.Register("claude-code", @"C:\repo\App.slnx");

        var servers = Load()["mcpServers"]!.AsObject();

        Assert.Equal("terse", servers["terse-sharp"]!["command"]!.GetValue<string>());
        Assert.Equal(
            ["serve", "--workspace", @"C:\repo\App.slnx"],
            servers["terse-sharp"]!["args"]!.AsArray().Select(node => node!.GetValue<string>()));
    }

    [Fact]
    public async Task Register_PreservesEveryOtherServer()
    {
        File.WriteAllText(ClaudeConfig, """{"mcpServers":{"other":{"command":"x"}},"unrelated":42}""");

        await ClientRegistrar.Register("claude-code", null);

        var root = Load();

        Assert.Equal(42, root["unrelated"]!.GetValue<int>());
        Assert.Equal("x", root["mcpServers"]!["other"]!["command"]!.GetValue<string>());
        Assert.NotNull(root["mcpServers"]!["terse-sharp"]);
    }

    [Fact]
    public async Task Unregister_RemovesOnlyTheTerseEntry()
    {
        File.WriteAllText(ClaudeConfig, """{"mcpServers":{"other":{"command":"x"}}}""");

        await ClientRegistrar.Register("claude-code", null);
        await ClientRegistrar.Unregister("claude-code");

        var servers = Load()["mcpServers"]!.AsObject();

        Assert.Null(servers["terse-sharp"]);
        Assert.NotNull(servers["other"]);
    }

    [Fact]
    public async Task Register_WithoutAWorkspace_OmitsTheWorkspaceArgument()
    {
        File.WriteAllText(ClaudeConfig, "{}");

        await ClientRegistrar.Register("claude-code", null);

        Assert.Equal(
            ["serve"],
            Load()["mcpServers"]!["terse-sharp"]!["args"]!.AsArray().Select(node => node!.GetValue<string>()));
    }

    [Fact]
    public async Task Register_WhenClaudeConfigDirectoryIsSet_WritesIntoThatDirectory()
    {
        var configDirectory = Path.Combine(home, "profile");

        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDirectory);

        await ClientRegistrar.Register("claude-code", null);

        Assert.False(File.Exists(ClaudeConfig));
        Assert.NotNull(LoadFrom(Path.Combine(configDirectory, ".claude.json"))["mcpServers"]!["terse-sharp"]);
    }

    [Fact]
    public async Task Unregister_WhenClaudeConfigDirectoryIsSet_RemovesFromThatDirectory()
    {
        var configDirectory = Path.Combine(home, "profile");

        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDirectory);

        await ClientRegistrar.Register("claude-code", null);
        await ClientRegistrar.Unregister("claude-code");

        Assert.Null(LoadFrom(Path.Combine(configDirectory, ".claude.json"))["mcpServers"]!["terse-sharp"]);
    }

    [Fact]
    public async Task InstallSkill_WhenClaudeConfigDirectoryIsSet_WritesUnderThatDirectory()
    {
        var configDirectory = Path.Combine(home, "profile");

        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDirectory);

        await ClientRegistrar.InstallSkill();

        Assert.True(File.Exists(Path.Combine(configDirectory, "skills", "terse-sharp", "SKILL.md")));
    }

    [Fact]
    public async Task InstallSkill_WithoutClaudeConfigDirectory_WritesUnderTheHomeClaudeDirectory()
    {
        await ClientRegistrar.InstallSkill();

        Assert.True(File.Exists(Path.Combine(home, ".claude", "skills", "terse-sharp", "SKILL.md")));
    }

    [Fact]
    public async Task State_MovesToRegisteredOnlyOnceTheEntryExistsInTheConfigInUse()
    {
        var configDirectory = Path.Combine(home, "profile");

        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDirectory);

        var target = ClaudeCode();

        Assert.Equal(ClientConfigState.NotFound, ClientRegistrar.State(target));

        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(target.ConfigPath, """{"mcpServers":{"other":{"command":"x"}}}""");

        Assert.Equal(ClientConfigState.NotRegistered, ClientRegistrar.State(target));

        await ClientRegistrar.Register("claude-code", null);

        Assert.Equal(ClientConfigState.Registered, ClientRegistrar.State(target));
    }

    [Fact]
    public void State_ReportsInvalidForAConfigThatIsNotValidJson()
    {
        File.WriteAllText(ClaudeConfig, Malformed);

        Assert.Equal(ClientConfigState.Invalid, ClientRegistrar.State(ClaudeCode()));
    }

    [Fact]
    public async Task Register_WithoutAClientAndAnUncreatedClaudeConfigDirectory_StillRegistersClaudeCode()
    {
        var configDirectory = Path.Combine(home, "profile");

        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDirectory);

        await ClientRegistrar.Register(null, null);

        Assert.Equal(ClientConfigState.Registered, ClientRegistrar.State(ClaudeCode()));
    }

    [Fact]
    public async Task Register_WhenNoClientMatches_SaysSoInsteadOfReturningNothing()
    {
        Assert.Equal("no MCP clients matched", await ClientRegistrar.Register("emacs", null));
    }
    [Fact]
    public async Task Register_WhenTheConfigIsNotValidJson_SkipsItAndLeavesTheFileUntouched()
    {
        File.WriteAllText(ClaudeConfig, Malformed);

        var message = await ClientRegistrar.Register("claude-code", null);

        Assert.Contains("skipped claude-code", message, StringComparison.Ordinal);
        Assert.Equal(Malformed, File.ReadAllText(ClaudeConfig));
    }
    [Fact]
    public async Task Unregister_WhenTheConfigIsNotValidJson_SkipsItAndLeavesTheFileUntouched()
    {
        File.WriteAllText(ClaudeConfig, Malformed);

        var message = await ClientRegistrar.Unregister("claude-code");

        Assert.Contains("skipped claude-code", message, StringComparison.Ordinal);
        Assert.Equal(Malformed, File.ReadAllText(ClaudeConfig));
    }
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TERSE_HOME", null);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfigDirectory);
        Directory.Delete(home, recursive: true);
    }

    private static ClientTarget ClaudeCode() =>
        ClientRegistrar.Known().Single(target => target.Name is "claude-code");

    private static JsonObject LoadFrom(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private string ClaudeConfig => Path.Combine(home, ".claude.json");

    private JsonObject Load() => LoadFrom(ClaudeConfig);
}
