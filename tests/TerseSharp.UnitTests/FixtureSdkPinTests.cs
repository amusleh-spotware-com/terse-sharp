using System.Text.Json;

namespace TerseSharp.UnitTests;

public sealed class FixtureSdkPinTests
{
    [Fact]
    public async Task EveryFixtureCopiedIntoATempRoot_PinsTheSameSdkBandAsTheRepository()
    {
        var repository = await PinAsync(Path.Combine(Fixtures.RepositoryRoot, "global.json"));
        var fixture = await PinAsync(Path.Combine(Fixtures.RepositoryRoot, "fixtures", "FixtureSolution", "global.json"));

        Assert.Equal(repository, fixture);
        Assert.Equal(("10.0.300", "latestPatch"), fixture);
    }

    private static async Task<(string Version, string RollForward)> PinAsync(string path)
    {
        Assert.True(File.Exists(path), path);

        await using var stream = File.Open(path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Options = FileOptions.Asynchronous | FileOptions.SequentialScan });

        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        var sdk = document.RootElement.GetProperty("sdk");

        return (sdk.GetProperty("version").GetString()!, sdk.GetProperty("rollForward").GetString()!);
    }
}
