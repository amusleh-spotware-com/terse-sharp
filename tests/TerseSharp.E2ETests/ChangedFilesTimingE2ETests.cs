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

        var first = await CallAsync("changed_files", new(arguments));
        var second = first;

        for (var attempt = 0; attempt < 30 && !second.Contains("UNCHANGED", StringComparison.Ordinal); attempt++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            second = await CallAsync("changed_files", new(arguments));
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
}
