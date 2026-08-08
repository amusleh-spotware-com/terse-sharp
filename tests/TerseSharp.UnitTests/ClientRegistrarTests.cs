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
    public async Task Register_WhenNoClientMatches_SaysSoInsteadOfReturningNothing() =>
        Assert.Equal("no MCP clients matched", await ClientRegistrar.Register("emacs", null));
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

    private string SkillFile => Path.Combine(home, ".claude", "skills", "terse-sharp", "SKILL.md");

    private string SettingsFile => Path.Combine(home, ".claude", "settings.json");

    [Fact]
    public async Task RefreshAsync_WhenTheInstalledSkillIsStale_RewritesItFromTheShippedAsset()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SkillFile)!);
        await File.WriteAllTextAsync(SkillFile, "# an older skill", TestContext.Current.CancellationToken);

        var refreshed = await ClientRegistrar.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Contains("installed skill", refreshed!, StringComparison.Ordinal);
        Assert.Equal(SkillAsset.Read(), await File.ReadAllTextAsync(SkillFile, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefreshAsync_WhenTheSkillWasNeverInstalled_DoesNotCreateIt()
    {
        Assert.Null(await ClientRegistrar.RefreshAsync(TestContext.Current.CancellationToken));
        Assert.False(File.Exists(SkillFile));
    }

    private async Task WriteSettingsAsync(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);

        await File.WriteAllTextAsync(SettingsFile, content, TestContext.Current.CancellationToken);
    }

    private static string? Matcher(JsonNode? entry) => entry?["matcher"]?.GetValue<string>();

    [Fact]
    public async Task RefreshAsync_WhenTheInstalledGuardIsStale_RewritesItAndKeepsEveryOtherHook()
    {
        await WriteSettingsAsync("""
            {"hooks":{"PreToolUse":[{"matcher":"Read","hooks":[{"type":"command","command":"terse guard"}]},{"matcher":"Bash","hooks":[{"type":"command","command":"other-tool"}]}]}}
            """);

        var refreshed = await ClientRegistrar.RefreshAsync(TestContext.Current.CancellationToken);
        var matchers = LoadFrom(SettingsFile)["hooks"]!["PreToolUse"]!.AsArray();

        Assert.Contains("installed guard", refreshed!, StringComparison.Ordinal);
        Assert.Contains(matchers, entry => entry!["hooks"]![0]!["command"]!.GetValue<string>() is "other-tool");
        Assert.Contains(matchers, entry => Matcher(entry) is "Read|Write|Edit|MultiEdit|NotebookEdit|Grep|Glob|Bash");
        Assert.DoesNotContain(matchers, entry => Matcher(entry) is "Read");
    }

    [Fact]
    public async Task RefreshAsync_WhenNoGuardIsInstalled_LeavesTheSettingsFileUntouched()
    {
        const string Settings = """{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"other-tool"}]}]}}""";

        await WriteSettingsAsync(Settings);

        Assert.Null(await ClientRegistrar.RefreshAsync(TestContext.Current.CancellationToken));
        Assert.Equal(Settings, await File.ReadAllTextAsync(SettingsFile, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AssetsAsync_ReportsWhatIsInstalledAndWhetherItMatchesThisBuild()
    {
        Assert.Equal(new AssetState(false, false, false, false), await ClientRegistrar.AssetsAsync(TestContext.Current.CancellationToken));

        await ClientRegistrar.InstallSkill();
        await ClientRegistrar.InstallGuard();

        Assert.Equal(new AssetState(true, true, true, true), await ClientRegistrar.AssetsAsync(TestContext.Current.CancellationToken));

        await File.WriteAllTextAsync(SkillFile, "# an older skill", TestContext.Current.CancellationToken);

        Assert.Equal(new AssetState(true, false, true, true), await ClientRegistrar.AssetsAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("""{"hooks":[]}""")]
    [InlineData("""{"hooks":"yes"}""")]
    [InlineData("""{"hooks":{"PreToolUse":"all"}}""")]
    [InlineData("""{"hooks":{"PreToolUse":["terse guard"]}}""")]
    [InlineData("""{"hooks":{"PreToolUse":[{"matcher":"Read","hooks":["terse guard"]}]}}""")]
    public async Task AssetsAsync_WhenTheSettingsShapeIsUnexpected_ReportsNoGuardInsteadOfThrowing(string settings)
    {
        await WriteSettingsAsync(settings);

        var state = await ClientRegistrar.AssetsAsync(TestContext.Current.CancellationToken);

        Assert.False(state.GuardInstalled);
        Assert.Null(await ClientRegistrar.RefreshAsync(TestContext.Current.CancellationToken));
        Assert.Equal(settings, await File.ReadAllTextAsync(SettingsFile, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void NeedsInstall_IsTrueForEveryAssetKindThatIsMissingOrStale()
    {
        var parameters = typeof(AssetState).GetConstructors().Single().GetParameters();

        Assert.NotEmpty(parameters);
        Assert.Equal(0, parameters.Length % 2);
        Assert.All(parameters, parameter => Assert.Equal(typeof(bool), parameter.ParameterType));

        for (var kind = 0; kind < parameters.Length / 2; kind++)
        {
            Assert.EndsWith("Installed", parameters[kind * 2].Name, StringComparison.Ordinal);
            Assert.EndsWith("Current", parameters[(kind * 2) + 1].Name, StringComparison.Ordinal);
            Assert.True(Assets(parameters.Length, index => index != kind * 2).NeedsInstall, parameters[kind * 2].Name);
            Assert.True(Assets(parameters.Length, index => index != (kind * 2) + 1).NeedsInstall, parameters[(kind * 2) + 1].Name);
        }

        Assert.False(Assets(parameters.Length, _ => true).NeedsInstall);
    }

    private static AssetState Assets(int length, Func<int, bool> value) => (AssetState)typeof(AssetState)
        .GetConstructors()
        .Single()
        .Invoke([.. Enumerable.Range(0, length).Select(index => (object)value(index))]);
}
