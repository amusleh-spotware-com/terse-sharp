namespace TerseSharp.UnitTests;

public sealed class E2ECollectionCensusTests
{
    private const string Membership = "[Collection(nameof(TerseServerCollection))]";

    private static readonly string[] BuildingCalls =
        ["\"build\"", "\"run_tests\"", "\"rerun_failed\"", "\"list_tests\"", "\"clean\""];

    private static readonly string Root =
        Path.Combine(Fixtures.RepositoryRoot, "tests", "TerseSharp.E2ETests");

    [Fact]
    public void EveryE2ETestClassThatSpawnsABuild_JoinsTheCollectionThatSerializesThem()
    {
        var building = Building();

        Assert.NotEmpty(building);
        Assert.Empty(building.Where(file => !file.Text.Contains(Membership, StringComparison.Ordinal)).Select(file => file.Name));
    }

    private static (string Name, string Text)[] Building() =>
    [
        .. Directory.EnumerateFiles(Root, "*.cs")
            .Select(file => (Name: Path.GetFileName(file), Text: File.ReadAllText(file)))
            .Where(file => Builds(file.Text)),
    ];

    private static bool Builds(string text) =>
        text.Contains("[Fact]", StringComparison.Ordinal)
        && BuildingCalls.Any(call => text.Contains(call, StringComparison.Ordinal));
}
