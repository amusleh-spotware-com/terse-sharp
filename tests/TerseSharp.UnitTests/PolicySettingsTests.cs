using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class PolicySettingsTests
{
    [Fact]
    public void Parse_WithNoPolicySection_LeavesPolicyOff()
    {
        var options = PolicySettings.Parse("""{"tools":{"groups":{"xaml":false}}}""");

        Assert.False(options.Enabled);
        Assert.False(options.Active);
    }

    [Fact]
    public void Parse_WithEnabledFalse_LeavesPolicyOff()
    {
        var options = PolicySettings.Parse("""{"policy":{"enabled":false,"rules":{"methodStatements":4}}}""");

        Assert.False(options.Active);
    }

    [Fact]
    public void Parse_WithAnEmptyPolicySection_TakesTheRiderDerivedDefaults()
    {
        var options = PolicySettings.Parse("""{"policy":{}}""");

        Assert.True(options.Active);
        Assert.Equal(10, options.CognitiveThreshold);
        Assert.Equal(150, options.Limit(PolicyRule.CognitiveComplexity).Value);
        Assert.Equal(15, options.CognitiveLimit());
        Assert.Equal(10, options.Limit(PolicyRule.MethodStatements).Value);
        Assert.Equal(5, options.Limit(PolicyRule.ConstructorDependencies).Value);
        Assert.Equal(3, options.Limit(PolicyRule.MethodNameLength).Value);
    }

    [Fact]
    public void Parse_WithNoUniformAction_KeepsTheSeverityEachRiderInspectionCarried()
    {
        var options = PolicySettings.Parse("""{"policy":{}}""");

        Assert.Equal(PolicyAction.Reject, options.Limit(PolicyRule.CognitiveComplexity).Action);
        Assert.Equal(PolicyAction.Warn, options.Limit(PolicyRule.MethodStatements).Action);
        Assert.Equal(PolicyAction.Warn, options.Limit(PolicyRule.ParameterCount).Action);
        Assert.Equal(PolicyAction.Reject, options.Limit(PolicyRule.Naming).Action);
    }

    [Fact]
    public void Parse_WithActionWarn_TurnsEveryRuleIntoAWarning()
    {
        var options = PolicySettings.Parse("""{"policy":{"action":"warn"}}""");

        Assert.All(PolicyRules.All, info => Assert.Equal(PolicyAction.Warn, options.Limit(info.Rule).Action));
    }

    [Fact]
    public void Parse_WithAPerRuleAction_OutranksTheUniformOne()
    {
        var options = PolicySettings.Parse("""{"policy":{"action":"warn","rules":{"naming":{"action":"reject"}}}}""");

        Assert.Equal(PolicyAction.Reject, options.Limit(PolicyRule.Naming).Action);
        Assert.Equal(PolicyAction.Warn, options.Limit(PolicyRule.MethodStatements).Action);
    }

    [Fact]
    public void Parse_WithARuleAsANumber_TakesItAsTheLimitAndKeepsTheAction()
    {
        var options = PolicySettings.Parse("""{"policy":{"rules":{"cognitiveComplexity":200}}}""");

        Assert.Equal(200, options.Limit(PolicyRule.CognitiveComplexity).Value);
        Assert.Equal(20, options.CognitiveLimit());
    }

    [Fact]
    public void Parse_WithARuleAsFalse_TurnsThatRuleOffAndLeavesTheRest()
    {
        var options = PolicySettings.Parse("""{"policy":{"rules":{"nestingDepth":false}}}""");

        Assert.False(options.Enforces(PolicyRule.NestingDepth));
        Assert.True(options.Enforces(PolicyRule.CognitiveComplexity));
    }

    [Fact]
    public void Parse_WithAnUnknownRuleKey_NamesItRatherThanDroppingItSilently()
    {
        var options = PolicySettings.Parse("""{"policy":{"rules":{"methodLenght":10}}}""");

        Assert.Contains("methodLenght", options.Ignored);
        Assert.NotNull(PolicySettings.Notice(options));
    }

    [Fact]
    public void Parse_WithAllowOverrideFalse_RecordsThatTheEscapeHatchIsClosed()
    {
        var options = PolicySettings.Parse("""{"policy":{"allowOverride":false}}""");

        Assert.False(options.AllowOverride);
    }

    [Fact]
    public void Parse_WithACustomNamingPattern_ReplacesOnlyThatKind()
    {
        var options = PolicySettings.Parse("""{"policy":{"naming":{"field":"^_[a-z][A-Za-z0-9]*$"}}}""");

        Assert.Equal("^_[a-z][A-Za-z0-9]*$", options.Naming[NamingKind.Field].Expression);
        Assert.Equal(NamingDefaults.Expressions[NamingKind.Interface], options.Naming[NamingKind.Interface].Expression);
    }

    [Fact]
    public void Parse_WithANamingPatternThatIsNotAValidRegex_NamesItRatherThanThrowing()
    {
        var options = PolicySettings.Parse("""{"policy":{"naming":{"field":"^[a-z"}}}""");

        Assert.Contains("field", options.Ignored);
        Assert.Equal(NamingDefaults.Expressions[NamingKind.Field], options.Naming[NamingKind.Field].Expression);
    }

    [Fact]
    public void Parse_WithCustomMeaninglessSuffixes_ReplacesTheRiderList()
    {
        var options = PolicySettings.Parse("""{"policy":{"meaninglessSuffixes":["Util"]}}""");

        Assert.Equal("Util", Assert.Single(options.MeaninglessSuffixes));
    }

    [Fact]
    public void Parse_WithMalformedJson_ReportsTheFailureAndLeavesPolicyOff()
    {
        var options = PolicySettings.Parse("{\"policy\":");

        Assert.NotNull(options.Failure);
        Assert.False(options.Active);
        Assert.NotNull(PolicySettings.Notice(options));
    }

    [Fact]
    public void Parse_WithAnActionThatIsNotARecognisedWord_NamesItRatherThanGuessing()
    {
        var options = PolicySettings.Parse("""{"policy":{"action":"block"}}""");

        Assert.Contains("action", options.Ignored);
        Assert.Equal(PolicyAction.Reject, options.Limit(PolicyRule.CognitiveComplexity).Action);
    }
}
