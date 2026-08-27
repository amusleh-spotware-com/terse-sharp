using Microsoft.CodeAnalysis;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class PolicyGateTests
{
    [Fact]
    public async Task ApplyAsync_WithNoTerseJson_LeavesPolicyOffAndAppliesTheEdit()
    {
        using var workspace = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        var result = await AddAsync(workspace, "public int Go() => 1;", allowPolicy: false);

        Assert.True(result.IsOk);
        Assert.DoesNotContain("policy", result.Value!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyAsync_ForAnEditThatIntroducesAViolation_RollsItBackAndNamesTheRuleAndTheFix()
    {
        using var workspace = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await ConfigureAsync(workspace, """{"policy":{}}""");

        var result = await AddAsync(workspace, "public int Go() => 1;", allowPolicy: false);

        Assert.False(result.IsOk);
        Assert.Equal(TerseErrorCode.PolicyViolation, result.Error!.Code);
        Assert.Contains("TERSE105", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("'Go' is 2 characters", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("fix: ", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("allowPolicy=true", result.Error.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_ForAViolationWithAllowPolicy_AppliesTheEditAndNamesEveryRuleItBypassed()
    {
        using var workspace = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await ConfigureAsync(workspace, """{"policy":{}}""");

        var result = await AddAsync(workspace, "public int Go() => 1;", allowPolicy: true);

        Assert.True(result.IsOk);
        Assert.Contains("WARNING policy overridden", result.Value!, StringComparison.Ordinal);
        Assert.Contains("TERSE105", result.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_ForAViolationWhenTheProjectForbidsOverriding_RefusesTheOverrideAndSaysSo()
    {
        using var workspace = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await ConfigureAsync(workspace, """{"policy":{"allowOverride":false}}""");

        var result = await AddAsync(workspace, "public int Go() => 1;", allowPolicy: true);

        Assert.False(result.IsOk);
        Assert.Contains("allowOverride=false", result.Error!.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_WithActionWarn_AppliesTheEditAndReportsTheRuleAsAWarning()
    {
        using var workspace = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await ConfigureAsync(workspace, """{"policy":{"action":"warn"}}""");

        var result = await AddAsync(workspace, "public int Go() => 1;", allowPolicy: false);

        Assert.True(result.IsOk);
        Assert.Contains("WARNING policy  TERSE105", result.Value!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_ForACleanEditIntoAFileThatAlreadyViolates_DoesNotChargeThePreExistingViolation()
    {
        using var workspace = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await ConfigureAsync(workspace, """{"policy":{}}""");
        Assert.True((await AddAsync(workspace, "public int Go() => 1;", allowPolicy: true)).IsOk);

        var result = await AddAsync(workspace, "public int SecondMember() => 2;", allowPolicy: false);

        Assert.True(result.IsOk);
        Assert.DoesNotContain("TERSE105", result.Value!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_WithTheRuleTurnedOff_AppliesTheSameEditWithNoFinding()
    {
        using var workspace = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await ConfigureAsync(workspace, """{"policy":{"rules":{"methodNameLength":false}}}""");

        var result = await AddAsync(workspace, "public int Go() => 1;", allowPolicy: false);

        Assert.True(result.IsOk);
        Assert.DoesNotContain("TERSE105", result.Value!, StringComparison.Ordinal);
    }

    private static async Task ConfigureAsync(TemporaryWorkspace workspace, string json)
    {
        PolicyCache.Forget();

        await File.WriteAllTextAsync(
            Path.Combine(workspace.Files.Root, TerseConfigFile.FileName),
            json,
            TestContext.Current.CancellationToken);
    }

    private static async Task<Result<string>> AddAsync(TemporaryWorkspace workspace, string declaration, bool allowPolicy)
    {
        var type = await ContainingTypeAsync(workspace);

        return await SymbolEditService.AddMemberAsync(
            workspace.Workspace,
            type,
            declaration,
            new EditOptions("add_member", DryRun: false, AllowErrors: false, AllowPolicy: allowPolicy),
            TestContext.Current.CancellationToken);
    }

    private static async Task<ISymbol> ContainingTypeAsync(TemporaryWorkspace workspace)
    {
        var project = workspace.Workspace.Solution.Projects.First(candidate => candidate.Name is "Fixture.Trading");
        var compilation = await project.GetCompilationAsync(TestContext.Current.CancellationToken);

        return compilation!.GetTypeByMetadataName("Fixture.Trading.OrderService")!;
    }

    [Fact]
    public async Task AnalyzeAsync_WithPolicyOn_ReportsTheViolationAsItsTerseRuleId()
    {
        using var workspace = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await ConfigureAsync(workspace, """{"policy":{}}""");
        Assert.True((await AddAsync(workspace, "public int Go() => 1;", allowPolicy: true)).IsOk);

        var report = await AnalysisService.AnalyzeAsync(
            workspace.Workspace,
            workspace.Files.OrderServicePath,
            DiagnosticSeverity.Info,
            [],
            includeDeadCode: false,
            maxResults: 200,
            sinceLast: false,
            changed: false,
            TestContext.Current.CancellationToken);

        Assert.Contains("TERSE105 warning Policy", report, StringComparison.Ordinal);
        Assert.Contains("OrderService.Go", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeAsync_WithNoPolicyConfigured_ReportsNoPolicyFinding()
    {
        using var workspace = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        PolicyCache.Forget();
        Assert.True((await AddAsync(workspace, "public int Go() => 1;", allowPolicy: false)).IsOk);

        var report = await AnalysisService.AnalyzeAsync(
            workspace.Workspace,
            workspace.Files.OrderServicePath,
            DiagnosticSeverity.Info,
            [],
            includeDeadCode: false,
            maxResults: 200,
            sinceLast: false,
            changed: false,
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain("TERSE1", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_WithAMalformedTerseJson_SaysSoOnTheEditInsteadOfSilentlyLeavingPolicyOff()
    {
        using var workspace = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await ConfigureAsync(workspace, "{\"policy\":");

        var result = await AddAsync(workspace, "public int Go() => 1;", allowPolicy: false);

        Assert.True(result.IsOk);
        Assert.Contains("could not be read", result.Value!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_WithAnUnknownRuleKey_NamesTheKeyOnTheEdit()
    {
        using var workspace = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await ConfigureAsync(workspace, """{"policy":{"rules":{"methodLenght":10}}}""");

        var result = await AddAsync(workspace, "public int Balance() => 1;", allowPolicy: false);

        Assert.True(result.IsOk);
        Assert.Contains("methodLenght", result.Value!, StringComparison.Ordinal);
    }
}
