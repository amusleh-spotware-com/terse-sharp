using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class PolicyDocumentTests
{
    [Fact]
    public void Render_DeclaresEveryRuleAtTheDefaultItsOwnCatalogueCarries()
    {
        var options = PolicySettings.Parse(PolicyDocument.Render());

        Assert.Empty(options.Ignored);
        Assert.Null(options.Failure);

        foreach (var info in PolicyRules.All)
        {
            Assert.Equal(info.Action, options.Limit(info.Rule).Action);
            Assert.Equal(info.Default, options.Limit(info.Rule).Value);
        }
    }

    [Fact]
    public void Render_CarriesTheThresholdAndTheOverrideSwitch()
    {
        var options = PolicySettings.Parse(PolicyDocument.Render());

        Assert.Equal(PolicyRules.CognitiveThreshold, options.CognitiveThreshold);
        Assert.True(options.AllowOverride);
    }

    [Fact]
    public void TopUp_ForAFileMissingARule_AddsItWithoutTouchingWhatIsAlreadyDeclared()
    {
        var updated = PolicyDocument.TopUp("""{"policy":{"rules":{"comments":{"action":"reject"}}}}""");

        Assert.NotNull(updated);

        var options = PolicySettings.Parse(updated);

        Assert.Equal(PolicyAction.Reject, options.Limit(PolicyRule.Comments).Action);
        Assert.Equal(PolicyRules.Of(PolicyRule.XmlDocs).Action, options.Limit(PolicyRule.XmlDocs).Action);
        Assert.Empty(options.Ignored);
    }

    [Fact]
    public void TopUp_ForAFileThatAlreadyDeclaresEveryRule_ChangesNothing() =>
        Assert.Null(PolicyDocument.TopUp(PolicyDocument.Render()));

    [Fact]
    public void TopUp_ForAFileThatIsNotJson_LeavesItAlone() =>
        Assert.Null(PolicyDocument.TopUp("{ not json"));

    [Fact]
    public void TopUp_ForAPolicyThatIsNotAnObject_LeavesItAlone() =>
        Assert.Null(PolicyDocument.TopUp("""{"policy":7}"""));

    [Fact]
    public void TopUp_ForAFileCarryingOnlyToolSettings_KeepsThemAndAddsThePolicy()
    {
        var updated = PolicyDocument.TopUp("""{"tools":{"groups":{"xaml":false}}}""");

        Assert.NotNull(updated);
        Assert.Contains("\"xaml\"", updated, StringComparison.Ordinal);
        Assert.Contains("\"xmlDocs\"", updated, StringComparison.Ordinal);
    }
}
