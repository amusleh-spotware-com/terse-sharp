namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class ToolCensusE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task EveryAdvertisedTool_IsProbedOrExemptWithAWrittenReason()
    {
        var accounted = ToolHappyPathE2ETests.Accounted();
        var missing = (await Advertised()).Where(name => !accounted.Contains(name)).ToArray();

        Assert.True(
            missing.Length is 0,
            "no probe and no written exemption for: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task NoExemptionSurvivesTheToolItNames()
    {
        var advertised = (await Advertised()).ToHashSet(StringComparer.Ordinal);
        var stale = ToolCensus.HappyPathExempt
            .Concat(ToolCensus.RobustnessExcluded)
            .Select(exemption => exemption.Tool)
            .Concat(ToolCensus.VerdictPrefixed.Select(verdict => verdict.Tool))
            .Concat(ToolCensus.BudgetOverrides.Select(budget => budget.Tool))
            .Where(tool => !advertised.Contains(tool))
            .ToArray();

        Assert.True(stale.Length is 0, "exempted but no longer advertised: " + string.Join(", ", stale));
    }

    [Fact]
    public void EveryExemptionCarriesAReasonAndTheSetOnlyEverShrinks()
    {
        var reasons = ToolCensus.HappyPathExempt
            .Concat(ToolCensus.RobustnessExcluded)
            .Select(exemption => (exemption.Tool, exemption.Reason))
            .Concat(ToolCensus.VerdictPrefixed.Select(verdict => (verdict.Tool, verdict.Reason)))
            .Concat(ToolCensus.BudgetOverrides.Select(budget => (budget.Tool, budget.Reason)));

        Assert.All(reasons, entry => Assert.True(
            entry.Reason.Length >= 40,
            entry.Tool + " is exempt without a written reason"));

        Assert.True(
            ToolCensus.HappyPathExempt.Length <= ToolCensus.MaxHappyPathExemptions,
            $"the happy-path exemption set is a ratchet: {ToolCensus.HappyPathExempt.Length} > {ToolCensus.MaxHappyPathExemptions}");

        Assert.True(
            ToolCensus.RobustnessExcluded.Length <= ToolCensus.MaxRobustnessExclusions,
            $"the robustness exclusion set is a ratchet: {ToolCensus.RobustnessExcluded.Length} > {ToolCensus.MaxRobustnessExclusions}");

        Assert.True(
            ToolCensus.VerdictPrefixed.Length <= ToolCensus.MaxVerdictPrefixes,
            $"the verdict-prefix set is a ratchet: {ToolCensus.VerdictPrefixed.Length} > {ToolCensus.MaxVerdictPrefixes}");

        Assert.True(
            ToolCensus.BudgetOverrides.Length <= ToolCensus.MaxBudgetOverrides,
            $"the budget-override set is a ratchet: {ToolCensus.BudgetOverrides.Length} > {ToolCensus.MaxBudgetOverrides}");
    }

    [Fact]
    public async Task EveryProcessSpawningTool_AnswersASuccessWithoutAHeaderAndWithinItsBudget()
    {
        foreach (var probe in ToolCensus.ProcessProbes)
        {
            var text = await server.CallAsync(probe.Tool, probe.Arguments);

            Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
            Assert.False(
                ToolCensus.OpensWithItsOwnName(probe.Tool, text),
                probe.Tool + " opened its response with its own name\n" + text);
            Assert.True(
                ToolCensus.Tokens(text) <= ToolCensus.Budget(probe.Tool),
                string.Create(CultureInfo.InvariantCulture, $"{probe.Tool}={ToolCensus.Tokens(text)}/{ToolCensus.Budget(probe.Tool)}\n{text}"));
        }
    }

    [Fact]
    public async Task EveryVerdictPrefixedTool_StillAnswersWithTheVerdictItIsExemptFor()
    {
        foreach (var verdict in ToolCensus.VerdictPrefixed)
        {
            var text = await server.CallAsync(verdict.Tool, verdict.Arguments);

            Assert.StartsWith(verdict.Prefix, text, StringComparison.Ordinal);
            Assert.DoesNotContain("\n", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task EveryProbedReadTool_StaysWithinItsTokenBudget()
    {
        var over = new List<string>();
        var probed = 0;

        foreach (var (tool, arguments, _) in ToolHappyPathE2ETests.Cases.Where(Reads))
        {
            var text = await server.CallAsync(tool, arguments);

            probed++;

            if (ToolCensus.Tokens(text) > ToolCensus.Budget(tool))
                over.Add(string.Create(CultureInfo.InvariantCulture, $"{tool}={ToolCensus.Tokens(text)}/{ToolCensus.Budget(tool)}"));
        }

        Assert.True(probed >= 40, $"only {probed} read tools were budgeted");
        Assert.True(over.Count is 0, "over budget: " + string.Join(", ", over));
    }

    private static bool Reads((string Tool, Dictionary<string, object?> Arguments, string Expect) probe) =>
        !probe.Arguments.ContainsKey("dryRun");

    private async Task<string[]> Advertised() =>
        [.. (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).Select(tool => tool.Name)];
}
