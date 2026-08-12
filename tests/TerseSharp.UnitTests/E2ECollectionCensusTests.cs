namespace TerseSharp.UnitTests;

public sealed class E2ECollectionCensusTests
{
    private const string Membership = "[Collection(nameof(TerseServerCollection))]";

    private const int MaxExclusions = 5;

    private static readonly string[] BuildingCalls =
        ["\"build\"", "\"run_tests\"", "\"rerun_failed\"", "\"list_tests\"", "\"clean\""];

    private static readonly Dictionary<string, string> Excluded = new(StringComparer.Ordinal)
    {
        ["ChangedTestSelectionE2ETests.cs"] = "joining the collection turned run 31634146607 red on ubuntu and windows - changed=true fell back to the whole solution, total=2 against its asserted total=1 - while it is green outside it",
        ["AnalyzerLockE2ETests.cs"] = "reverted with ChangedTestSelection: it spawns its own external dotnet build to prove the analyzer shadow copy, which is the one case that wants its own scheduling slot",
        ["MarkupProfileE2ETests.cs"] = "reverted with ChangedTestSelection; it loads a markup-free solution to measure the narrowed surface and never competes for a fixture's obj/",
        ["ProjectFileIntegrityE2ETests.cs"] = "reverted with ChangedTestSelection; it asserts on .csproj bytes, not on what a build emitted",
        ["ReadOnlyServerE2ETests.cs"] = "reverted with ChangedTestSelection; it runs a --read-only server whose every mutating call is refused before any build",
    };

    private static readonly string Root =
        Path.Combine(Fixtures.RepositoryRoot, "tests", "TerseSharp.E2ETests");

    [Fact]
    public void EveryE2ETestClassThatSpawnsABuild_JoinsTheCollectionThatSerializesThem()
    {
        var building = Building();
        var missing = building
            .Where(file => !Excluded.ContainsKey(file.Name) && !file.Text.Contains(Membership, StringComparison.Ordinal))
            .Select(file => file.Name);

        Assert.NotEmpty(building);
        Assert.Empty(missing);
    }

    [Fact]
    public void TheExclusionSetCarriesAReasonPerEntryAndOnlyEverShrinks()
    {
        var names = Building().Select(file => file.Name).ToHashSet(StringComparer.Ordinal);

        Assert.True(Excluded.Count <= MaxExclusions, "the exclusion set is a ratchet: " + Excluded.Count + " > " + MaxExclusions);
        Assert.All(Excluded, entry => Assert.True(entry.Value.Length > 40, entry.Key + " carries no reason"));
        Assert.All(Excluded, entry => Assert.Contains(entry.Key, names));
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
