using System.Text;
using System.Text.RegularExpressions;

namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed partial class DocsCoverageE2ETests(TerseServerFixture server)
{
    [Theory]
    [InlineData("README.md")]
    [InlineData("NUGET_README.md")]
    [InlineData("src/TerseSharp.Server/Assets/SKILL.md")]
    public async Task EveryAdvertisedTool_IsNamedInTheShippedDocumentation(string document)
    {
        var path = Path.Combine(TerseServerFixture.RepositoryRoot, document.Replace('/', Path.DirectorySeparatorChar));
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        var advertised = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var missing = advertised.Select(tool => tool.Name).Where(name => !Names(text, name)).ToArray();

        Assert.True(missing.Length is 0, string.Create(CultureInfo.InvariantCulture, $"{document} does not name: {string.Join(", ", missing)}"));
    }

    private static bool Names(string text, string tool)
    {
        var quoted = string.Concat("`", tool);

        for (var index = text.IndexOf(quoted, StringComparison.Ordinal); index >= 0; index = text.IndexOf(quoted, index + 1, StringComparison.Ordinal))
        {
            var following = index + quoted.Length;

            if (following >= text.Length || (!char.IsLetterOrDigit(text[following]) && text[following] is not '_'))
                return true;
        }

        return false;
    }

    [Fact]
    public async Task TheShippedSkill_StaysWithinItsTokenBudget()
    {
        var path = Path.Combine(
        TerseServerFixture.RepositoryRoot,
        Path.Combine("src", "TerseSharp.Server", "Assets", "SKILL.md"));

        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var tokens = ToolCensus.Tokens(text);

        Assert.True(
            tokens <= SkillTokenBudget,
            string.Create(CultureInfo.InvariantCulture, $"SKILL.md costs {tokens} tokens, budget {SkillTokenBudget}\n{ToolCensus.BudgetProbe}"));
    }

    [Fact]
    public async Task TheShippedSkill_EnumeratesEveryToolExactlyOnceInOneTable()
    {
        var path = Path.Combine(
            TerseServerFixture.RepositoryRoot,
            Path.Combine("src", "TerseSharp.Server", "Assets", "SKILL.md"));

        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var advertised = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var rows = text.Split('\n').Where(line => line.StartsWith("| ", StringComparison.Ordinal)).ToArray();
        var absent = advertised.Select(tool => tool.Name).Where(name => !rows.Any(row => Names(row, name))).ToArray();

        Assert.True(rows.Length >= 90, string.Create(CultureInfo.InvariantCulture, $"only {rows.Length} table rows were found"));
        Assert.True(absent.Length is 0, "tools no table row names: " + string.Join(", ", absent));
    }

    private const int SkillTokenBudget = 25000;

    [Theory]
    [InlineData("README.md")]
    [InlineData("NUGET_README.md")]
    public async Task EveryTokenCeilingTheDocsClaim_IsAssertedByATest(string document)
    {
        var path = Path.Combine(TerseServerFixture.RepositoryRoot, document);
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var claims = TokenClaim().Matches(text).Select(Claimed).Distinct().ToArray();
        var sources = await TestSourcesAsync();
        var unasserted = claims.Where(claim => !sources.Any(source => Asserts(source, claim))).ToArray();

        Assert.True(claims.Length > 0, document + " claims no token ceiling - update this census when the claims are deliberately removed");
        Assert.True(unasserted.Length is 0, document + " claims token ceilings no test under tests/ asserts: " + string.Join(", ", unasserted));
    }

    private static string Claimed(Match match) => match.Groups["n"].Value.Replace(",", "", StringComparison.Ordinal);

    private static bool Asserts(string source, string claim) =>
        source.Contains(claim, StringComparison.Ordinal) || source.Contains(Separated(claim), StringComparison.Ordinal);

    private static string Separated(string claim)
    {
        var builder = new StringBuilder(claim.Length + claim.Length / 3);
        for (var index = 0; index < claim.Length; index++)
        {
            if (index > 0 && (claim.Length - index) % 3 == 0)
                builder.Append('_');

            builder.Append(claim[index]);
        }

        return builder.ToString();
    }

    private static async Task<string[]> TestSourcesAsync()
    {
        var sources = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(TerseServerFixture.RepositoryRoot, "tests"), "*.cs", SearchOption.AllDirectories))
            sources.Add(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));

        return [.. sources];
    }

    [GeneratedRegex(@"≤\s?(?<n>\d{1,3}(?:,\d{3})+|\d{4,})\s?tokens|(?<n>\d{1,3}(?:,\d{3})+|\d{4,})-token ceiling")]
    private static partial Regex TokenClaim();
}
