using ModelContextProtocol.Client;

namespace TerseSharp.E2ETests;

public sealed class BuildWarningsE2ETests : IAsyncLifetime
{
    private static readonly string[] DeliberateWarningCodes = ["CS0169", "CS0414", "CS0219"];

    private static readonly string WarningRoot =
        Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "WarningSolution");

    private static readonly string CalculatorPath =
        Path.Combine(WarningRoot, "src", "Fixture.Warning", "Calculator.cs");

    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync() =>
        server = await TerseServerProcess.StartAsync(
            WarningRoot,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--workspace", Path.Combine(WarningRoot, "WarningSolution.slnx")],
            TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => server.StopAsync();

    [Fact]
    public async Task Build_WhenTheSolutionCompilesWithWarnings_AnswersInOneLineAndNamesNone()
    {
        var text = await RebuiltAsync([]);

        Assert.StartsWith("build ok  errors=0 warnings=3", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0169", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0414", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0219", text, StringComparison.Ordinal);
        Assert.True(text.Length < 120, text);
    }

    [Fact]
    public async Task Build_WhenVerboseIsAsked_ListsEveryWarningTheCompilerReported()
    {
        var text = await RebuiltAsync(new() { ["verbose"] = true });

        Assert.Contains("CS0169", text, StringComparison.Ordinal);
        Assert.Contains("CS0414", text, StringComparison.Ordinal);
        Assert.Contains("CS0219", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBuildAndTestFamily_IsDiscoveredFromTheAdvertisedSurface()
    {
        var family = await FamilyAsync();

        Assert.True(family.Length >= 4, "the discovered build/test family was too small: " + Named(family));
    }

    [Fact]
    public async Task EveryBuildAndTestTool_HidesTheCompilerWarningsUnlessVerboseIsAsked()
    {
        var family = await FamilyAsync();
        var leaking = new List<string>();
        var refused = new List<string>();

        Assert.True(family.Length >= 4, "the discovered build/test family was too small: " + Named(family));

        await RebuiltAsync("run_tests", []);

        foreach (var tool in family)
        {
            var text = await RebuiltAsync(tool, []);

            if (text.Contains("ERROR", StringComparison.Ordinal))
                refused.Add(tool + " -> " + text);

            if (Leaks(text))
                leaking.Add(tool + " -> " + text);
        }

        Assert.True(refused.Count is 0, "tools that never reached a build, so the sweep proved nothing: " + string.Join(" | ", refused));
        Assert.True(leaking.Count is 0, "tools returning a compiler warning without verbose: " + string.Join(" | ", leaking));
    }

    private async Task<string[]> FamilyAsync()
    {
        var surface = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        return [.. surface.Where(Scoped).Select(tool => tool.Name).Order(StringComparer.Ordinal)];
    }

    private static string Named(string[] family) =>
        family.Length is 0 ? "(none)" : string.Join(", ", family);

    private static bool Scoped(McpClientTool tool) =>
        Declares(tool, "configuration") && Declares(tool, "targetFramework");

    private static bool Declares(McpClientTool tool, string parameter) =>
        tool.JsonSchema.TryGetProperty("properties", out var properties)
        && properties.TryGetProperty(parameter, out _);

    private static bool Leaks(string text) =>
        text.Contains(": warning ", StringComparison.Ordinal)
        || DeliberateWarningCodes.Any(code => text.Contains(code, StringComparison.Ordinal));

    private Task<string> RebuiltAsync(Dictionary<string, object?> arguments) => RebuiltAsync("build", arguments);

    private Task<string> RebuiltAsync(string tool, Dictionary<string, object?> arguments)
    {
        File.SetLastWriteTimeUtc(CalculatorPath, DateTime.UtcNow);

        return server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Build_WithAnMsBuildProperty_AppliesItToTheBuild()
    {
        var text = await RebuiltAsync(new() { ["properties"] = new[] { "TreatWarningsAsErrors=true" } });

        Assert.Contains("exitCode=1", text, StringComparison.Ordinal);
        Assert.Contains("CS0169", text, StringComparison.Ordinal);
        Assert.DoesNotContain("build ok", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--interactive")]
    [InlineData("")]
    [InlineData("=NoName")]
    [InlineData("NoSeparator")]
    [InlineData(null)]
    public async Task Build_WithAPropertyThatIsNotNameEqualsValue_IsRefusedWithARemedy(string? property)
    {
        var text = await server.CallAsync(
            "build",
            new() { ["properties"] = new[] { property } },
            TestContext.Current.CancellationToken);

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("is not Name=Value", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WithAnMsBuildProperty_AppliesItToTheBuildUnderTheRun()
    {
        var text = await RebuiltAsync("run_tests", new() { ["properties"] = new[] { "TreatWarningsAsErrors=true" } });

        Assert.Contains("CS0169", text, StringComparison.Ordinal);
        Assert.DoesNotContain("run_tests PASSED", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListTests_WithAnMsBuildProperty_AppliesItToTheBuildUnderTheListing()
    {
        var text = await RebuiltAsync("list_tests", new() { ["properties"] = new[] { "TreatWarningsAsErrors=true" } });

        Assert.Contains("CS0169", text, StringComparison.Ordinal);
    }
}
