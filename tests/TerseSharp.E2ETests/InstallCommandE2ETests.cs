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
        Assert.Contains("'path' is required", output, StringComparison.Ordinal);
        Assert.Contains("spelled path or paths", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Doctor_AttributesThePerCallFloorToTheOutlineTheCompileGateAndTheGitSpawn()
    {
        var output = await RunAsync("doctor", "--workspace", Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"));

        var line = output
            .Split('\n')
            .Single(candidate => candidate.StartsWith("OK   phases: ", StringComparison.Ordinal));

        Assert.Contains("widest=src", line, StringComparison.Ordinal);
        Assert.Contains(".cs ", line, StringComparison.Ordinal);
        Assert.DoesNotContain("obj", line, StringComparison.Ordinal);
        Assert.DoesNotContain(".Designer.cs", line, StringComparison.Ordinal);
        Assert.Contains("outlineMs=", line, StringComparison.Ordinal);
        Assert.Contains("gateMs=", line, StringComparison.Ordinal);
        Assert.Contains("diffMs=", line, StringComparison.Ordinal);
        Assert.DoesNotContain("outlineMs=0.00", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Doctor_NamesTheRunningAssemblyAndTheOneShotProbeCommand()
    {
        var output = await RunAsync("doctor", "--workspace", Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"));
        var assembly = output
            .Split('\n')
            .Select(line => line.IndexOf("assembly=", StringComparison.Ordinal) is var start and >= 0 ? line[(start + 9)..] : null)
            .FirstOrDefault(value => value is { Length: > 0 });

        Assert.NotNull(assembly);
        Assert.StartsWith(TerseServerFixture.ServerAssemblyPath(), assembly, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("call <tool> --workspace <solution> --json", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Doctor_FromTheApphost_PrintsAProbeCommandThatDoesNotGoThroughTheMuxer()
    {
        var apphost = Apphost();

        Assert.True(File.Exists(apphost), "the apphost is missing: " + apphost);

        var start = new ProcessStartInfo(apphost)
        {
            WorkingDirectory = TerseServerFixture.FixtureRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add("doctor");
        start.ArgumentList.Add("--workspace");
        start.ArgumentList.Add(Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"));
        start.Environment["TERSE_HOME"] = home;
        start.Environment["CLAUDE_CONFIG_DIR"] = ConfigDirectory;

        var output = await ReadAsync(start);

        Assert.Contains("assembly=" + apphost, output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("probe: dotnet ", output, StringComparison.Ordinal);
        Assert.Contains("call <tool> --workspace <solution> --json", output, StringComparison.Ordinal);
    }

    private static string Apphost()
    {
        var assembly = TerseServerFixture.ServerAssemblyPath();

        return OperatingSystem.IsWindows()
            ? Path.ChangeExtension(assembly, ".exe")
            : Path.Combine(Path.GetDirectoryName(assembly)!, Path.GetFileNameWithoutExtension(assembly));
    }

    [Fact]
    public async Task Doctor_ComparesTheRoslynThisBuildCarriesAgainstTheOneTheSdkCarries()
    {
        var output = await RunAsync("doctor", "--workspace", Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"));

        Assert.Contains("roslyn: terse carries Microsoft.CodeAnalysis ", output, StringComparison.Ordinal);
        Assert.Contains("the selected SDK carries ", output, StringComparison.Ordinal);
        Assert.Contains("OK   roslyn:", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Doctor_ReportsWhetherTheGuardCoversThisTreesMeasuredBreachClasses()
    {
        var output = await RunAsync("doctor", "--workspace", Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"));

        Assert.Contains("guard coverage: read-cs=denied", output, StringComparison.Ordinal);
        Assert.Contains("git-status=denied", output, StringComparison.Ordinal);
        Assert.Contains("OK   guard coverage:", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Doctor_SeparatesTheOncePerLoadCompilationFromThePerCallPhases()
    {
        var output = await RunAsync("doctor", "--workspace", Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"));
        var line = output.Split('\n').Single(candidate => candidate.Contains(" phases: ", StringComparison.Ordinal));

        Assert.Contains("realizeMs=", line, StringComparison.Ordinal);
        Assert.True(Phase(line, "realizeMs") > Phase(line, "outlineMs"), line);
        Assert.True(Phase(line, "outlineMs") > 0, line);
    }

    private static double Phase(string line, string name)
    {
        var start = line.IndexOf(name, StringComparison.Ordinal) + name.Length + 1;
        var end = line.IndexOf(' ', start);

        return double.Parse(end < 0 ? line[start..] : line[start..end], CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Call_WithAnArgumentTheToolDoesNotDeclare_RefusesItExactlyAsTheServerWould()
    {
        var output = await RunAsync(
            "call",
            "analyze",
            "--workspace",
            Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"),
            "--json",
            "{\"minSeverityLevel\": \"info\"}");

        Assert.Contains("ERROR InvalidArgument", output, StringComparison.Ordinal);
        Assert.Contains("analyze rejected the call: unrecognized minSeverityLevel", output, StringComparison.Ordinal);
        Assert.Contains("minSeverity", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheVeryFirstCallOfAProcess_AlreadyCarriesTheAbsentGuardWarning()
    {
        var server = await TerseServerProcess.StartAsync(
            TerseServerFixture.FixtureRoot,
            [
                TerseServerFixture.ServerAssemblyPath(),
            "serve",
            "--workspace",
            Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"),
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TERSE_HOME"] = home,
                ["CLAUDE_CONFIG_DIR"] = ConfigDirectory,
                ["TERSE_UPDATE"] = "0",
            },
            TestContext.Current.CancellationToken);

        try
        {
            var first = await server.CallAsync(
                "workspace_status",
                new Dictionary<string, object?>(StringComparer.Ordinal),
                TestContext.Current.CancellationToken);

            Assert.Contains("WARNING guard=absent", first, StringComparison.Ordinal);
            Assert.Contains("terse install --guard", first, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }
}
