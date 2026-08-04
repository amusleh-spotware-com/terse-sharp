namespace TerseSharp.E2ETests;

public sealed class BuildWarningsE2ETests : IAsyncLifetime
{
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

    private Task<string> RebuiltAsync(Dictionary<string, object?> arguments)
    {
        File.SetLastWriteTimeUtc(CalculatorPath, DateTime.UtcNow);

        return server.CallAsync("build", arguments, TestContext.Current.CancellationToken);
    }
}
