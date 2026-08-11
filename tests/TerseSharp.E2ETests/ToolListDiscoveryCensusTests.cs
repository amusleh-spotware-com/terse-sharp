namespace TerseSharp.E2ETests;

public sealed class ToolListDiscoveryCensusTests
{
    private const int MinimumDiscoveringFiles = 9;

    private static readonly ToolExemption[] Exempt =
[
    new("ToolProfileE2ETests.cs", "it exists to assert what --tools core advertises, so seeing the narrowed list is its whole subject"),
    new("ToolListDiscoveryCensusTests.cs", "it reads the other files' source text and never calls the server itself"),
    new("MarkupProfileE2ETests.cs", "it exists to assert the surface a solution holding no .xaml, .razor or .resx advertises, so seeing the narrowed list is its whole subject"),
];

    private const int MaxDiscoveryExemptions = 3;

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
        Assert.True(Exempt.Length <= MaxDiscoveryExemptions, $"the exemption set grew to {Exempt.Length}");
        Assert.All(Exempt, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Reason), entry.Tool));
        Assert.Empty(shrunk);
    }

    private static string SourceDirectory { get; } =
        Path.Combine(TerseServerFixture.RepositoryRoot, "tests", "TerseSharp.E2ETests");

    private static void Route(string name, string text, List<string> discovering, List<string> shrunk)
    {
        if (!text.Contains("ListToolsAsync", StringComparison.Ordinal) || Array.Exists(Exempt, entry => string.Equals(entry.Tool, name, StringComparison.Ordinal)))
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
