using TerseSharp.Core;

namespace TerseSharp.E2ETests;

public sealed class UpdateE2ETests : IAsyncLifetime
{
    private const string Marker = "UPDATE terse ";
    private const string StaleSkill = "# an older skill";

    private readonly string home = Path.Combine(Path.GetTempPath(), "terse-e2e", Guid.NewGuid().ToString());
    private readonly List<TerseServerProcess> servers = [];
    private readonly List<StubReleaseFeed> feeds = [];

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(ConfigDirectory);

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ANewerRelease_IsAnnouncedOnceOnTheNextToolResponse()
    {
        var feed = Feed("v99.9.9");
        var server = await StartAsync(feed, checks: true);

        var announced = await AnnouncedAsync(server);

        Assert.Contains(Marker, announced, StringComparison.Ordinal);
        Assert.Contains("-> 99.9.9 is available - run: dotnet tool update -g TerseSharp", announced, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, await CallAsync(server), StringComparison.Ordinal);
        Assert.Equal(1, feed.Requests);
        Assert.Contains("99.9.9", await ReadAsync(StateFile), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAnnouncementIsOneLineAppendedToTheResponseTheToolWouldHaveGivenAnyway()
    {
        var feed = Feed("v99.9.9");

        await WriteAsync(StateFile, new UpdateState(DateTimeOffset.UtcNow, new ReleaseVersion(42, 0, 0, false)).Render());

        var server = await StartAsync(feed, checks: true);

        var announced = await AnnouncedAsync(server);
        var lines = announced.Split('\n');

        Assert.StartsWith("0 workspaces", announced, StringComparison.Ordinal);
        Assert.Equal(1, lines.Count(line => line.StartsWith(Marker, StringComparison.Ordinal)));
        Assert.Equal(lines[^1], lines.Single(line => line.StartsWith(Marker, StringComparison.Ordinal)));
        Assert.True(lines[^1].Length < 120, lines[^1]);
        Assert.Equal(0, feed.Requests);
    }

    [Fact]
    public async Task ARunningVersionThatIsUpToDate_AddsNothingToAnyResponse()
    {
        var feed = Feed("v0.0.1");
        var server = await StartAsync(feed, checks: true);

        await CheckedAsync();
        await StaysQuietAsync(server);

        Assert.Equal(1, feed.Requests);
    }

    [Fact]
    public async Task WithTheChecksTurnedOff_TheReleaseFeedIsNeverContactedAndNoStateIsWritten()
    {
        var feed = Feed("v99.9.9");
        var server = await StartAsync(feed, checks: false);

        await StaysQuietAsync(server);

        Assert.Equal(0, feed.Requests);
        Assert.False(File.Exists(StateFile));
    }

    [Fact]
    public async Task AFreshStateFile_AnswersWithoutContactingTheReleaseFeedAtAll()
    {
        var feed = Feed("v99.9.9");

        await WriteAsync(StateFile, new UpdateState(DateTimeOffset.UtcNow, new ReleaseVersion(42, 0, 0, false)).Render());

        var server = await StartAsync(feed, checks: true);

        Assert.Contains("-> 42.0.0 is available", await AnnouncedAsync(server), StringComparison.Ordinal);
        Assert.Equal(0, feed.Requests);
    }

    [Fact]
    public async Task OnStartup_AnInstalledSkillAndGuardAreRewrittenToMatchTheRunningBuild()
    {
        const string StaleGuard = """{"hooks":{"PreToolUse":[{"matcher":"Read","hooks":[{"type":"command","command":"terse guard"}]}]}}""";

        await WriteAsync(SkillFile, StaleSkill);
        await WriteAsync(SettingsFile, StaleGuard);

        await StartAsync(Feed("v0.0.1"), checks: true);

        var skill = await RewrittenAsync(SkillFile, StaleSkill);
        var settings = await RewrittenAsync(SettingsFile, StaleGuard);

        Assert.Contains("get_file_outline", skill, StringComparison.Ordinal);
        Assert.Contains("Read|Write|Edit|MultiEdit|NotebookEdit|Grep|Glob|Bash", settings, StringComparison.Ordinal);
        Assert.Contains("terse guard", settings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnStartup_ASkillThatWasNeverInstalledIsNotCreated()
    {
        await StartAsync(Feed("v0.0.1"), checks: true);
        await CheckedAsync();

        Assert.False(File.Exists(SkillFile));
        Assert.False(File.Exists(SettingsFile));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var server in servers)
            await server.StopAsync();

        foreach (var feed in feeds)
            feed.Dispose();

        Directory.Delete(home, recursive: true);
    }

    private string ConfigDirectory => Path.Combine(home, ".claude-profile");

    private string StateFile => Path.Combine(home, ".terse", "update");

    private string SkillFile => Path.Combine(ConfigDirectory, "skills", "terse-sharp", "SKILL.md");

    private string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    private StubReleaseFeed Feed(string tag)
    {
        var feed = new StubReleaseFeed(tag);

        feeds.Add(feed);

        return feed;
    }

    private async Task<TerseServerProcess> StartAsync(StubReleaseFeed feed, bool checks)
    {
        var server = await TerseServerProcess.StartAsync(
            home,
            [TerseServerFixture.ServerAssemblyPath(), "serve"],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TERSE_UPDATE"] = checks ? "1" : "0",
                ["TERSE_UPDATE_URL"] = feed.Endpoint,
                ["TERSE_HOME"] = home,
                ["CLAUDE_CONFIG_DIR"] = ConfigDirectory,
            },
            TestContext.Current.CancellationToken);

        servers.Add(server);

        return server;
    }

    private static Task<string> CallAsync(TerseServerProcess server) =>
        server.CallAsync("list_workspaces", new Dictionary<string, object?>(StringComparer.Ordinal), TestContext.Current.CancellationToken);

    private static async Task<string> AnnouncedAsync(TerseServerProcess server)
    {
        for (var attempt = 0; attempt < 600; attempt++)
        {
            var response = await CallAsync(server);

            if (response.Contains(Marker, StringComparison.Ordinal))
                return response;

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new InvalidOperationException("no update notice reached a tool response");
    }

    private async Task CheckedAsync()
    {
        for (var attempt = 0; attempt < 600 && !File.Exists(StateFile); attempt++)
            await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(StateFile), "the update check never recorded its state");
    }

    private static async Task<string> ReadAsync(string path) =>
        await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

    private static async Task WriteAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
    }

    private static async Task StaysQuietAsync(TerseServerProcess server)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            Assert.DoesNotContain(Marker, await CallAsync(server), StringComparison.Ordinal);

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private static async Task<string> RewrittenAsync(string path, string stale)
    {
        for (var attempt = 0; attempt < 600; attempt++)
        {
            var content = await ReadAsync(path);

            if (!string.Equals(content, stale, StringComparison.Ordinal))
                return content;

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new InvalidOperationException(path + " was never rewritten");
    }
}
