namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class RemainingToolsE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task GetTypeOutline_ListsTheMembersWithoutBodies()
    {
        var text = await server.CallAsync("get_type_outline", new() { ["symbolId"] = "T:Fixture.Trading.OrderService" });

        Assert.Contains("  OrderService.Submit  ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("repository.Submit(order)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTypeOutline_WithAMemberId_ReturnsAnActionableError()
    {
        var text = await server.CallAsync("get_type_outline", new() { ["symbolId"] = "M:Fixture.Trading.OrderService.Unused" });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("An error occurred invoking", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchRegex_WithAnUnparseablePattern_ReturnsAnActionableError()
    {
        var text = await server.CallAsync("search_regex", new() { ["pattern"] = "([a-z" });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.DoesNotContain("An error occurred invoking", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbol_WithDryRun_ShowsTheNewDeclaration()
    {
        var text = await server.CallAsync("replace_symbol", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Unused",
            ["declaration"] = "public int Unused() => 11;",
            ["dryRun"] = true,
        });

        Assert.Contains("+", text, StringComparison.Ordinal);
        Assert.Contains("11", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_ReportsTheExitCodeAndNoMsBuildSpew()
    {
        var text = await server.CallAsync("build", new() { ["verbose"] = true });

        Assert.Contains("exitCode=0", text, StringComparison.Ordinal);
        Assert.Contains("0 diagnostics", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Determining projects to restore", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_WithAProjectOutsideTheWorkspace_IsRefused()
    {
        var outside = Path.Combine(TerseServerFixture.RepositoryRoot, "src", "TerseSharp.Core", "TerseSharp.Core.csproj");

        var text = await server.CallAsync("build", new() { ["project"] = outside });

        Assert.Contains("ERROR OutOfWorkspace", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_OnAProjectlessFixture_ReportsTheOutcomeNotSilence()
    {
        var text = await server.CallAsync("run_tests", []);

        Assert.Contains("exitCode=", text, StringComparison.Ordinal);
        Assert.Contains("total=0", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_OnABinaryFile_IsRefused()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "src/Fixture.Trading/bin/Debug/net10.0/Fixture.Trading.dll",
        });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.DoesNotContain("This program cannot be run in DOS mode", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_OnACsFile_IsRefusedWithoutForce()
    {
        var text = await server.CallAsync("write_text", new()
        {
            ["path"] = "src/Fixture.Trading/Order.cs",
            ["content"] = "// clobbered",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("replace_symbol_body", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_WhenMoreMatchThanTheCap_ReportsTheRealTotal()
    {
        var capped = await server.CallAsync("find_files", new() { ["glob"] = "*.cs", ["maxResults"] = 2 });
        var all = await server.CallAsync("find_files", new() { ["glob"] = "*.cs", ["maxResults"] = 500 });

        var total = Total(capped);

        Assert.Equal(Total(all), total);
        Assert.Contains(" truncated", capped, StringComparison.Ordinal);
        Assert.True(total > 3, capped);
    }

    [Fact]
    public async Task UnloadWorkspace_ThenReload_Works()
    {
        var solution = Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx");

        Assert.Contains("unloaded", await server.CallAsync("unload_workspace", new() { ["path"] = solution }), StringComparison.Ordinal);

        var reloaded = await server.CallAsync("load_workspace", new() { ["path"] = solution });

        Assert.Contains("projects=1", reloaded, StringComparison.Ordinal);
    }

    private static int Total(string response)
    {
        var newline = response.IndexOf('\n');
        var summary = response.AsSpan(0, newline < 0 ? response.Length : newline);
        var slash = summary.IndexOf('/');
        var counted = slash < 0 ? summary : summary[(slash + 1)..];

        return int.Parse(counted[..counted.IndexOf(' ')], CultureInfo.InvariantCulture);
    }
    [Fact]
    public async Task Build_WhenClean_AnswersInOneLineUnlessVerboseIsAsked()
    {
        var quiet = await server.CallAsync("build", []);

        Assert.StartsWith("build ok", quiet, StringComparison.Ordinal);
        Assert.Contains("errors=0 warnings=0", quiet, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", quiet, StringComparison.Ordinal);
        Assert.True(quiet.Length < 120, quiet);
    }

    [Fact]
    public async Task FindFiles_WithPatternInsteadOfGlob_AnswersTheSameAsGlob()
    {
        var byGlob = await server.CallAsync("find_files", new() { ["glob"] = "*.csproj" });
        var byPattern = await server.CallAsync("find_files", new() { ["pattern"] = "*.csproj" });

        Assert.Equal(byGlob, byPattern);
        Assert.Contains("Fixture.Trading.csproj", byPattern, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_WithNeitherGlobNorPattern_AnswersAStructuredError()
    {
        var text = await server.CallAsync("find_files", []);

        Assert.StartsWith("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("An error occurred invoking", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnloadWorkspace_WithWorkspaceInsteadOfPath_UnloadsAndThenReloads()
    {
        var solution = Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx");

        var unloaded = await server.CallAsync("unload_workspace", new() { ["workspace"] = solution });

        Assert.Contains("unloaded", unloaded, StringComparison.Ordinal);
        Assert.DoesNotContain("An error occurred invoking", unloaded, StringComparison.Ordinal);
        Assert.Contains("projects=1", await server.CallAsync("load_workspace", new() { ["path"] = solution }), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListProjects_WithAFilter_KeepsOnlyMatchingProjects()
    {
        var matching = await server.CallAsync("list_projects", new() { ["filter"] = "Trading" });
        var missing = await server.CallAsync("list_projects", new() { ["filter"] = "Hosting" });

        Assert.Contains("Fixture.Trading", matching, StringComparison.Ordinal);
        Assert.StartsWith("1 projects", matching, StringComparison.Ordinal);
        Assert.Equal("0 projects", missing);
        Assert.DoesNotContain("Fixture.Trading", missing, StringComparison.Ordinal);
    }
}
