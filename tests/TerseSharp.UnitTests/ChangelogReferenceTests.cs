using System.Text.RegularExpressions;

namespace TerseSharp.UnitTests;

public sealed partial class ChangelogReferenceTests
{
    private static readonly string[] Lines =
        File.ReadAllLines(Path.Combine(Fixtures.RepositoryRoot, "CHANGELOG.md"));

    private static readonly HashSet<string> Excluded = new(StringComparer.Ordinal);

    [Fact]
    public void TheChangelog_NamesOnlyTestsThatStillExist()
    {
        var declared = Declared();
        var dead = Referenced().Where(name => !declared.Contains(name)).ToArray();

        Assert.True(
            dead.Length is 0,
            "the newest changelog sections name tests that no longer exist: " + string.Join(", ", dead));
    }

    [Fact]
    public void TheChangelog_ReallyNamesTests() => Assert.NotEmpty(Referenced());

    [Fact]
    public void EveryExclusionStillNamesSomethingTheChangelogReferences()
    {
        var referenced = Referenced().ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(Excluded, name => !referenced.Contains(name));
    }

    private static string[] Referenced() =>
    [
        .. Newest()
        .SelectMany(Quoted)
        .Select(Simple)
        .Where(IsTestName)
        .Where(name => !Excluded.Contains(name))
        .Distinct(StringComparer.Ordinal),
    ];

    private static IEnumerable<string> Newest()
    {
        var sections = 0;

        foreach (var line in Lines)
        {
            if (line.StartsWith("## [", StringComparison.Ordinal))
                sections++;

            if (sections is > 0 and <= 2)
                yield return line;
        }
    }

    private static IEnumerable<string> Quoted(string line)
    {
        var parts = line.Split('`');

        for (var index = 1; index < parts.Length; index += 2)
            yield return parts[index];
    }

    private static string Simple(string reference) =>
        reference[(reference.LastIndexOf('.') + 1)..].TrimStart('…');

    private static bool IsTestName(string name) =>
            name.Length > 0
            && char.IsAsciiLetterUpper(name[0])
            && name.Contains('_', StringComparison.Ordinal)
            && name.Any(char.IsAsciiLetterLower)
            && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_');

    private static HashSet<string> Declared()
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var root = Path.Combine(Fixtures.RepositoryRoot, "tests");

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            foreach (Match match in Declaration().Matches(File.ReadAllText(file)))
                declared.Add(match.Groups[1].Value);
        }

        return declared;
    }

    [GeneratedRegex(@"\b(?:public|private|internal|protected)\s+(?:static\s+)?(?:async\s+)?(?:Task|ValueTask|void)\s+(\w+)\s*\(")]
    private static partial Regex Declaration();

    [Theory]
    [InlineData("ReplaceSymbol_RolledBackByASignatureChange_NamesTheDeclarationsThatCallIt", true)]
    [InlineData("EditToolsE2ETests.ReplaceSymbol_DryRunOfASignatureChange_NamesTheCallersItWouldBreak", true)]
    [InlineData("…AddMember_DryRunForAMissingUsing_StillNamesTheImportItWouldNeed", true)]
    [InlineData("DiffText_NeverReturnsMoreLinesThanMaxLines", true)]
    [InlineData("TokenBudgetE2ETests.TheAdvertisedToolPayload_StaysWithinItsBudget", true)]
    [InlineData("TERSE_RESULTS_DIRECTORY", false)]
    [InlineData("get_file_outline", false)]
    [InlineData("RESX002", false)]
    [InlineData("Strings.Designer.cs", false)]
    [InlineData("CS7036", false)]
    public void ATestNameIsToldApartFromEveryOtherBackTickedThingTheChangelogCarries(string reference, bool expected) => Assert.Equal(expected, IsTestName(Simple(reference)));
}
