namespace TerseSharp.E2ETests;

public sealed class ChangedFilesTimingE2ETests : IAsyncLifetime
{
    private static readonly string FixtureRoot = Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "FixtureSolution");

    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync() =>
        server = await TerseServerProcess.StartAsync(
            FixtureRoot,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(FixtureRoot, "FixtureSolution.slnx")],
            new Dictionary<string, string> { ["TERSE_LISTING_TIMING_MS"] = "0" },
            TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => server.StopAsync();

    [Fact]
    public async Task ChangedFiles_OverTheSlowThreshold_NamesEveryGitPhase_AndItsUnchangedReplayCarriesNoStaleTiming()
    {
        var arguments = new Dictionary<string, object?> { ["path"] = "notes.md" };

        var first = await CallAsync("changed_files", [with(arguments)]);
        var second = first;

        for (var attempt = 0; attempt < 150 && !second.Contains("UNCHANGED", StringComparison.Ordinal); attempt++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            second = await CallAsync("changed_files", [with(arguments)]);
        }

        Assert.Equal(1, first.Split("\ntiming stamp=").Length - 1);
        Assert.Contains(" numstat=", first, StringComparison.Ordinal);
        Assert.Contains(" name-status=", first, StringComparison.Ordinal);
        Assert.Contains(" ls-files=", first, StringComparison.Ordinal);
        Assert.Contains("UNCHANGED - no watcher event and no git state change", second, StringComparison.Ordinal);
        Assert.DoesNotContain("\ntiming ", second, StringComparison.Ordinal);
    }

    private Task<string> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);

    [Fact]
    public async Task SearchTextAndSearchRegex_OverTheSlowThreshold_EndWithOneTimingLineNamingTheIndexAndScanPhases()
    {
        var literal = await CallAsync("search_text", new() { ["query"] = "OrderService", ["glob"] = "src/**/*.cs" });
        var expression = await CallAsync("search_regex", new() { ["query"] = "class \\w+Service", ["glob"] = "src/**/*.cs" });

        Assert.Contains("OrderService", literal, StringComparison.Ordinal);
        Assert.Matches(@"^timing index=\d+ scan=\d+ files=[1-9]\d*$", Assert.Single(literal.Split('\n'), line => line.StartsWith("timing ", StringComparison.Ordinal)));
        Assert.Matches(@"^timing index=\d+ scan=\d+ files=[1-9]\d*$", Assert.Single(expression.Split('\n'), line => line.StartsWith("timing ", StringComparison.Ordinal)));
    }
}
