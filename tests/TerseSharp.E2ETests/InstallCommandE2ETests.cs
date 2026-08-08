using System.Diagnostics;

namespace TerseSharp.E2ETests;

public sealed class InstallCommandE2ETests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "terse-e2e", Guid.NewGuid().ToString());

    public InstallCommandE2ETests() => Directory.CreateDirectory(ConfigDirectory);

    [Fact]
    public async Task Install_WhenClaudeConfigDirectoryIsSet_WritesIntoTheConfigTheClientReads()
    {
        var output = await RunAsync("install", "--client", "claude-code");

        Assert.Contains(ConfigDirectory, output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(home, ".claude.json")));
        Assert.Contains("terse-sharp", await File.ReadAllTextAsync(ClaudeConfig, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_WithTheSkill_WritesTheSkillUnderTheConfigDirectory()
    {
        await RunAsync("install", "--client", "claude-code", "--skill");

        Assert.True(File.Exists(Path.Combine(ConfigDirectory, "skills", "terse-sharp", "SKILL.md")));
    }

    [Fact]
    public async Task Doctor_ReportsRegistrationAgainstTheConfigDirectoryInUse()
    {
        var workspace = Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx");
        var before = await RunAsync("doctor", "--workspace", workspace);

        await RunAsync("install", "--client", "claude-code");

        var after = await RunAsync("doctor", "--workspace", workspace);

        Assert.Contains("FAIL clients: terse-sharp not registered", before, StringComparison.Ordinal);
        Assert.Contains("OK   clients: claude-code -> " + ClaudeConfig, after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Doctor_WhenAClientConfigIsNotValidJson_ReportsItInsteadOfCrashing()
    {
        await File.WriteAllTextAsync(ClaudeConfig, "{ // a comment\n}", TestContext.Current.CancellationToken);

        var output = await RunAsync("doctor", "--workspace", Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"));

        Assert.DoesNotContain("Unhandled exception", output, StringComparison.Ordinal);
        Assert.Contains("FAIL clients: terse-sharp not registered; invalid JSON in claude-code -> " + ClaudeConfig, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Doctor_WhenAnotherClientConfigIsInvalid_StillReportsTheRegisteredClient()
    {
        var vsCodeConfig = Path.Combine(home, ".vscode", "mcp.json");

        Directory.CreateDirectory(Path.GetDirectoryName(vsCodeConfig)!);
        await File.WriteAllTextAsync(vsCodeConfig, "{ // a comment\n}", TestContext.Current.CancellationToken);
        await RunAsync("install", "--client", "claude-code");

        var output = await RunAsync("doctor", "--workspace", Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"));

        Assert.Contains("claude-code -> " + ClaudeConfig, output, StringComparison.Ordinal);
        Assert.Contains("invalid JSON in vscode -> " + vsCodeConfig, output, StringComparison.Ordinal);
    }

    public void Dispose() => Directory.Delete(home, recursive: true);

    private string ConfigDirectory => Path.Combine(home, ".claude-profile");

    private string ClaudeConfig => Path.Combine(ConfigDirectory, ".claude.json");

    private async Task<string> RunAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = TerseServerFixture.FixtureRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add(TerseServerFixture.ServerAssemblyPath());

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        start.Environment["TERSE_HOME"] = home;
        start.Environment["CLAUDE_CONFIG_DIR"] = ConfigDirectory;

        return await ReadAsync(start);
    }

    private static async Task<string> ReadAsync(ProcessStartInfo start)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");

        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return await output + await error;
    }

    [Fact]
    public async Task Doctor_ReportsTheLiveTerseAndTesthostProcesses()
    {
        var output = await RunAsync("doctor", "--workspace", Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"));

        var line = output
            .Split('\n')
            .Single(candidate => candidate.StartsWith("OK   processes: ", StringComparison.Ordinal));

        Assert.True(
            line.EndsWith("a server started as 'dotnet terse.dll' is not listed", StringComparison.Ordinal)
                || line.Contains("stop them and re-run", StringComparison.Ordinal),
            line);
    }

    [Fact]
    public async Task Call_AgainstTheBinaryUnderTest_AnswersTheToolItNamesForTheWorkspaceItWasGiven()
    {
        var output = await RunAsync(
            "call",
            "get_file_outline",
            "--workspace",
            Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"),
            "--json",
            "{\"path\": \"src/Fixture.Trading/OrderService.cs\"}");

        Assert.Contains("OrderService.Submit", output, StringComparison.Ordinal);
        Assert.Contains("public bool Submit(Order order)", output, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Call_WithATheServerDoesNotAdvertise_NamesSomeThatItDoes()
    {
        var output = await RunAsync("call", "grep_everything");

        Assert.Contains("ERROR InvalidArgument", output, StringComparison.Ordinal);
        Assert.Contains("no tool is named 'grep_everything'", output, StringComparison.Ordinal);
        Assert.Contains("remedy:", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Call_WithoutARequiredArgument_NamesItRatherThanThrowing()
    {
        var output = await RunAsync(
            "call",
            "read_text",
            "--workspace",
            Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"));

        Assert.Contains("ERROR InvalidArgument", output, StringComparison.Ordinal);
        Assert.Contains("missing required argument 'path'", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", output, StringComparison.Ordinal);
    }
}
