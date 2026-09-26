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
        var tokens = TerseSharp.Core.SkillBudget.Used(text);

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

    private const int SkillTokenBudget = ToolCensus.ShippedSkillBudget;

    [Theory]
    [InlineData("README.md")]
    [InlineData("NUGET_README.md")]
    public async Task EveryTokenCeilingTheDocsClaim_MatchesADeclaredSurfaceBudget(string document)
    {
        var path = Path.Combine(TerseServerFixture.RepositoryRoot, document);
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var claims = TokenClaim().Matches(text).Select(Claimed).Distinct().ToArray();
        var declared = ToolCensus.DeclaredBudgets;
        var undeclared = claims.Where(claim => !declared.Contains(claim)).ToArray();

        Assert.True(claims.Length > 0, document + " claims no token ceiling - update this census when the claims are deliberately removed");
        Assert.True(
            undeclared.Length is 0,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{document} claims token ceilings ToolCensus does not declare: {string.Join(", ", undeclared)} - declared: {string.Join(", ", declared)}"));
    }

    private static int Claimed(Match match) =>
        int.Parse(match.Groups["n"].Value.Replace(",", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);

    [GeneratedRegex(@"≤\s?(?<n>\d{1,3}(?:,\d{3})+|\d{4,})\s?tokens|(?<n>\d{1,3}(?:,\d{3})+|\d{4,})-token ceiling")]
    private static partial Regex TokenClaim();

    [Fact]
    public async Task ReadText_WithTokensOnTheShippedSkill_AnswersTheBudgetThisCensusEnforces()
    {
        var path = Path.Combine(TerseServerFixture.RepositoryRoot, "src", "TerseSharp.Server", "Assets", "SKILL.md");
        var used = TerseSharp.Core.SkillBudget.Used(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        var skill = await server.CallAsync("read_text", new() { ["path"] = path, ["tokens"] = true, ["lines"] = "1" });
        var readme = await server.CallAsync("read_text", new()
        {
            ["path"] = Path.Combine(TerseServerFixture.RepositoryRoot, "README.md"),
            ["tokens"] = true,
            ["lines"] = "1",
        });
        var remaining = SkillTokenBudget - used;
        var expected = remaining >= 0
            ? string.Create(CultureInfo.InvariantCulture, $"budget={SkillTokenBudget} used={used} left={remaining}")
            : string.Create(CultureInfo.InvariantCulture, $"budget={SkillTokenBudget} used={used} over={-remaining}");

        Assert.Equal(expected, BudgetLine(skill));
        Assert.DoesNotContain("budget=", readme, StringComparison.Ordinal);
    }

    private static string BudgetLine(string text) => text
        .Split('\n')
        .Select(line => line.TrimEnd('\r'))
        .Single(line => line.StartsWith("budget=", StringComparison.Ordinal));
}
