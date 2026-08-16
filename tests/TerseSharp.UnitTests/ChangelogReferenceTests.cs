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

    [Fact]
    public void EveryVersionHeading_HasALinkDefinition_AndEveryDefinitionNamesAHeading()
    {
        var headings = Headings();
        var definitions = Definitions();

        Assert.NotEmpty(headings);
        Assert.True(
            headings.All(definitions.ContainsKey),
            "version headings with no link definition: " + string.Join(", ", headings.Where(version => !definitions.ContainsKey(version))));
        Assert.True(
            definitions.Keys.All(version => version is "Unreleased" || headings.Contains(version)),
            "link definitions naming no heading: " + string.Join(", ", definitions.Keys.Where(version => version is not "Unreleased" && !headings.Contains(version))));
    }

    [Fact]
    public void EveryVersionOlderThanTheNewest_NamesATagThatExists()
    {
        var tags = Tags();
        var missing = Headings().Skip(1).Where(version => !tags.Contains("v" + version)).ToArray();

        Assert.NotEmpty(tags);
        Assert.True(
            missing.Length is 0,
            "the changelog names versions that were never tagged: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryTag_HasAVersionHeading()
    {
        var headings = Headings().ToHashSet(StringComparer.Ordinal);
        var orphans = Tags()
            .Where(tag => !Unreleased.Contains(tag) && !headings.Contains(tag.TrimStart('v')))
            .ToArray();

        Assert.True(orphans.Length is 0, "tags with no changelog heading: " + string.Join(", ", orphans));
    }

    [Fact]
    public void EveryTagWithoutAHeading_CarriesAReasonAndTheSetOnlyEverShrinks()
    {
        var tags = Tags();

        Assert.True(Unreleased.Count <= MaxUnreleasedTags, "the set of untagged-by-design versions may only shrink");
        Assert.All(Unreleased, tag => Assert.Contains(tag, tags));
        Assert.All(Unreleased, tag => Assert.NotEmpty(UnreleasedReason(tag)));
    }

    private const int MaxUnreleasedTags = 1;
    private static readonly HashSet<string> Unreleased = new(StringComparer.Ordinal) { "v0.15.1" };

    private static string UnreleasedReason(string tag) => tag switch
    {
        "v0.15.1" => "created on the 0.15.0 commit by mistake; the package is byte-identical to 0.15.0 and the 0.15.2 section says so, so there is nothing for a heading to describe",
        _ => string.Empty,
    };

    [Fact]
    public void TheUnreleasedComparison_PointsAtTheNewestVersion()
    {
        var newest = Headings()[0];

        Assert.EndsWith("compare/v" + newest + "...HEAD", Definitions()["Unreleased"], StringComparison.Ordinal);
    }

    private static string[] Headings() =>
    [
        .. Lines
        .Where(line => line.StartsWith("## [", StringComparison.Ordinal))
        .Select(line => line[4..line.IndexOf(']', StringComparison.Ordinal)])
        .Where(version => version is not "Unreleased"),
];

    private static Dictionary<string, string> Definitions()
    {
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in Lines)
        {
            var separator = line.IndexOf("]: ", StringComparison.Ordinal);

            if (line.StartsWith('[') && separator > 1)
                definitions[line[1..separator]] = line[(separator + 3)..].Trim();
        }

        return definitions;
    }

    private static HashSet<string> Tags()
    {
        var start = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = Fixtures.RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add("tag");
        start.ArgumentList.Add("--list");
        start.ArgumentList.Add("v*");

        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("git did not start");

        var output = process.StandardOutput.ReadToEnd();

        process.WaitForExit();

        return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(tag => tag.Trim())];
    }
}
