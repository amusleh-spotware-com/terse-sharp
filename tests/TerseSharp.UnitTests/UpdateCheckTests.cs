using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class UpdateCheckTests : IDisposable
{
    private const string Unreachable = "http://localhost:1/releases/latest";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "terse-tests", Guid.NewGuid().ToString());

    public UpdateCheckTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void Notice_WhenTheLatestReleaseIsNewer_NamesBothVersionsAndTheUpdateCommand() =>
        Assert.Equal(
            "UPDATE terse 0.15.2 -> 0.16.0 is available - run: dotnet tool update -g TerseSharp",
            UpdateCheck.Notice(new ReleaseVersion(0, 15, 2, false), new ReleaseVersion(0, 16, 0, false)));

    [Fact]
    public void Notice_WhenTheRunningVersionIsCurrentOrNewer_SaysNothing()
    {
        Assert.Null(UpdateCheck.Notice(new ReleaseVersion(0, 15, 2, false), new ReleaseVersion(0, 15, 2, false)));
        Assert.Null(UpdateCheck.Notice(new ReleaseVersion(0, 16, 0, false), new ReleaseVersion(0, 15, 2, false)));
    }

    [Fact]
    public void Notice_WhenTheLatestReleaseIsUnknown_SaysNothing() =>
        Assert.Null(UpdateCheck.Notice(new ReleaseVersion(0, 15, 2, false), null));

    [Fact]
    public void Notice_ForAPrereleaseAheadOfTheLatestRelease_SaysNothing() =>
        Assert.Null(UpdateCheck.Notice(new ReleaseVersion(0, 16, 0, true), new ReleaseVersion(0, 15, 2, false)));

    [Fact]
    public async Task RunAsync_InsideTheWindow_AnswersFromTheStateFileInsteadOfTheNetwork()
    {
        var recorded = new UpdateState(DateTimeOffset.UtcNow, new ReleaseVersion(9, 9, 9, false));

        await File.WriteAllTextAsync(StatePath, recorded.Render(), TestContext.Current.CancellationToken);

        var latest = await UpdateCheck.RunAsync(Request(TimeSpan.FromHours(24)), TestContext.Current.CancellationToken);

        Assert.Equal(new ReleaseVersion(9, 9, 9, false), latest);
    }

    [Fact]
    public async Task RunAsync_WhenTheProbeFails_RecordsTheAttemptSoTheNextCallStaysQuiet()
    {
        var latest = await UpdateCheck.RunAsync(Request(TimeSpan.Zero), TestContext.Current.CancellationToken);
        var written = await File.ReadAllTextAsync(StatePath, TestContext.Current.CancellationToken);

        Assert.Null(latest);
        Assert.True(UpdateState.TryParse(written, out var state));
        Assert.Null(state.Latest);
        Assert.True(DateTimeOffset.UtcNow - state.CheckedUtc < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RunAsync_WhenTheStateFileCannotBeParsed_ProbesInsteadOfThrowing()
    {
        await File.WriteAllTextAsync(StatePath, "not a state line", TestContext.Current.CancellationToken);

        Assert.Null(await UpdateCheck.RunAsync(Request(TimeSpan.FromHours(24)), TestContext.Current.CancellationToken));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private string StatePath => Path.Combine(directory, "update");

    private UpdateRequest Request(TimeSpan window) =>
        new(new ReleaseVersion(0, 15, 2, false), StatePath, Unreachable, window);
}
