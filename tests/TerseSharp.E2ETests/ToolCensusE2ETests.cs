using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using TerseSharp.Server;

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

    [Fact]
    public async Task EveryWorkedExample_NamesAnAdvertisedToolAndOnlyParametersThatToolDeclares()
    {
        var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var declared = tools.ToDictionary(tool => tool.Name, Parameters, StringComparer.Ordinal);
        var faults = new List<string>();

        foreach (var name in ToolExamples.Tools)
        {
            if (!declared.TryGetValue(name, out var accepted))
            {
                faults.Add(name + " is not advertised");
                continue;
            }

            var example = ToolExamples.For(name);
            faults.AddRange(Keys(example, name).Where(key => !accepted.Contains(key)).Select(key => name + " names " + key));

            if (!example.StartsWith(name + " ", StringComparison.Ordinal))
                faults.Add(name + " does not open with its own tool name");
        }

        Assert.True(faults.Count is 0, string.Join("; ", faults));
    }

    [Fact]
    public async Task EveryToolWithAWorkedExample_CarriesItInTheRemedyOfARejectedCall()
    {
        var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var demanding = tools.Where(tool => Demands(tool) is not 0).Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        var probed = ToolExamples.Tools.Where(demanding.Contains).ToArray();
        var missing = new List<string>();

        Assert.NotEmpty(probed);

        foreach (var name in probed)
        {
            var response = await server.CallAsync(name, []);

            if (!response.Contains("example: " + ToolExamples.For(name), StringComparison.Ordinal))
                missing.Add(name);
        }

        Assert.True(
            missing.Count is 0,
            "no worked example reached the remedy of: " + string.Join(", ", missing));
    }
    private static HashSet<string> Parameters(McpClientTool tool)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        if (tool.ProtocolTool.InputSchema.TryGetProperty("properties", out var properties)
            && properties.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
                names.Add(property.Name);
        }

        return names;
    }

    private static IEnumerable<string> Keys(string example, string tool) => example[(tool.Length + 1)..]
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(token => token.Contains('=', StringComparison.Ordinal))
        .Select(token => token[..token.IndexOf('=', StringComparison.Ordinal)])
        .Where(key => key.Length > 0 && char.IsLetter(key[0]));

    private static int Demands(McpClientTool tool) =>
        tool.ProtocolTool.InputSchema.TryGetProperty("required", out var required)
        && required.ValueKind is JsonValueKind.Array
            ? required.GetArrayLength()
            : 0;

    private const string Replaces = "Replaces Bash ";

    private static readonly string[] Drivers = ["git", "dotnet", "msbuild"];

    private static string[] Shell(string description) =>
        description.StartsWith(Replaces, StringComparison.Ordinal)
            ? description[Replaces.Length..].Split('.')[0].Split(" and ", StringSplitOptions.TrimEntries)
            : [];

    [Fact]
    public async Task EveryToolThatAdvertisesItReplacesAShellCommand_IsDeniedByTheGuard()
    {
        var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var replaced = tools.SelectMany(tool => Shell(tool.Description ?? string.Empty)).ToArray();
        var allowed = replaced
            .Where(command => !ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Denied)
            .ToArray();
        var unknown = replaced
            .Where(command => !Drivers.Contains(
                command.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty,
                StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            replaced.Length >= ToolCensus.MinShellReplacements,
            $"the enrolled shell-command set is a ratchet: {replaced.Length} < {ToolCensus.MinShellReplacements} - a description lost its 'Replaces Bash ' prefix and the tool is no longer census-gated");

        Assert.True(
            unknown.Length is 0,
            "extracted from a 'Replaces Bash ' description but does not start with a known driver, so the census is reading prose rather than a command: "
            + string.Join(", ", unknown));

        Assert.True(
            allowed.Length is 0,
            "advertised as replaced by a tool, but terse guard still allows it in Bash: " + string.Join(", ", allowed));
    }

    [Fact]
    public async Task EveryToolInTheCoreProfile_IsAToolTheServerAdvertises()
    {
        var advertised = (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ToolProfile.CoreTools.Where(name => !advertised.Contains(name)).ToArray();

        Assert.True(
            missing.Length is 0,
            "named by the core tool profile but not advertised by the server, so the profile would hide it forever: "
            + string.Join(", ", missing));

        Assert.True(ToolProfile.CoreTools.Count < advertised.Count, "the core profile must be a proper subset of the surface");
    }

    [Fact]
    public async Task NoTwoAdvertisedTools_DescribeThemselvesNearlyIdentically()
    {
        var surface = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var described = surface.Select(tool => (tool.Name, Words: ToolCensus.Words(tool.Description ?? string.Empty))).ToArray();
        var similar = new List<string>();
        var pairs = 0;

        for (var first = 0; first < described.Length; first++)
        {
            for (var second = first + 1; second < described.Length; second++)
            {
                pairs++;

                var score = ToolCensus.Overlap(described[first].Words, described[second].Words);

                if (score >= ToolCensus.RedundancyThreshold && !ToolCensus.SimilarByDesign(described[first].Name, described[second].Name))
                    similar.Add(string.Create(CultureInfo.InvariantCulture, $"{described[first].Name}~{described[second].Name}={score:F2}"));
            }
        }

        Assert.True(pairs > 1000, "the pairwise sweep saw too few pairs to be a census");
        Assert.True(
            similar.Count is 0,
            "advertised tools whose descriptions overlap past the redundancy threshold: " + string.Join(", ", similar));
    }

    [Fact]
    public async Task EverySimilarByDesignPair_StillNamesTwoAdvertisedTools()
    {
        var advertised = (await Advertised()).ToHashSet(StringComparer.Ordinal);
        var stale = ToolCensus.SimilarByDesignPairs
            .Where(pair => !advertised.Contains(pair.First) || !advertised.Contains(pair.Second))
            .Select(pair => pair.First + "~" + pair.Second)
            .ToArray();

        Assert.True(stale.Length is 0, "similar-by-design pairs naming a tool the server no longer advertises: " + string.Join(", ", stale));
        Assert.True(
            ToolCensus.SimilarByDesignPairs.Length <= ToolCensus.MaxSimilarByDesignPairs,
            "the similar-by-design set may only shrink");
        Assert.DoesNotContain(ToolCensus.SimilarByDesignPairs, pair => pair.Reason.Length is 0);
    }
}
