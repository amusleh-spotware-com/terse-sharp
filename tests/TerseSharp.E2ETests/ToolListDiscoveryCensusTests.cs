namespace TerseSharp.E2ETests;

public sealed class ToolListDiscoveryCensusTests
{
    private const int MinimumDiscoveringFiles = 9;

    private static readonly string[] Exempt =
    [
        "ToolProfileE2ETests.cs",
        "ToolListDiscoveryCensusTests.cs",
    ];

    [Fact]
    public async Task EveryTestThatDiscoversFromToolsList_SeesTheWholeSurfaceAndNotJustTheCoreProfile()
    {
        var discovering = new List<string>();
        var shrunk = new List<string>();

        foreach (var file in Directory.EnumerateFiles(SourceDirectory, "*.cs"))
        {
            var text = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);

            Route(Path.GetFileName(file), text, discovering, shrunk);
        }

        Assert.True(discovering.Count >= MinimumDiscoveringFiles, $"discovering={discovering.Count}");
        Assert.Empty(shrunk);
    }

    private static string SourceDirectory { get; } =
        Path.Combine(TerseServerFixture.RepositoryRoot, "tests", "TerseSharp.E2ETests");

    private static void Route(string name, string text, List<string> discovering, List<string> shrunk)
    {
        if (!text.Contains("ListToolsAsync", StringComparison.Ordinal) || Exempt.Contains(name, StringComparer.Ordinal))
            return;

        discovering.Add(name);

        if (SpawnsItsOwnServer(text) && !AsksForEveryTool(text))
            shrunk.Add(name);
    }

    private static bool SpawnsItsOwnServer(string text) =>
        text.Contains("TerseServerProcess.StartAsync", StringComparison.Ordinal);

    private static bool AsksForEveryTool(string text) =>
        text.Contains("\"--tools\",", StringComparison.Ordinal) && text.Contains("\"all\",", StringComparison.Ordinal);
}
