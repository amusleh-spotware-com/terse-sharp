using System.Collections.Frozen;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class PolicyServiceTests
{
    [Fact]
    public void Inspect_WithPolicyOff_FindsNothingHoweverBadTheCode()
    {
        var found = PolicyService.Inspect(Root("class m { void x() { int Q; } }"), "Sample.cs", PolicyOptions.Off);

        Assert.Empty(found);
    }

    [Fact]
    public void Inspect_ForAMemberAbove150PercentOfTheThreshold_ReportsCognitiveComplexity()
    {
        var found = Findings(PolicyRule.CognitiveComplexity, """
            class Sample
            {
                void Deep(bool a, bool b, bool c, bool d, bool e, bool f)
                {
                    if (a) { if (b) { if (c) { if (d) { if (e) { if (f) { } } } } } }
                }
            }
            """);

        var finding = Assert.Single(found);

        Assert.Equal("Sample.Deep", finding.Declaration);
        Assert.Contains("cognitive complexity 21", finding.Measured, StringComparison.Ordinal);
        Assert.Contains("210% of threshold 10", finding.Measured, StringComparison.Ordinal);
        Assert.Equal("150% (15)", finding.Allowed);
        Assert.Equal(PolicyAction.Reject, finding.Action);
    }

    [Fact]
    public void Inspect_ForAMemberAtOrBelow150PercentOfTheThreshold_ReportsNothing()
    {
        var found = Findings(PolicyRule.CognitiveComplexity, """
            class Sample
            {
                void Deep(bool a, bool b, bool c, bool d, bool e)
                {
                    if (a) { if (b) { if (c) { if (d) { if (e) { } } } } }
                }
            }
            """);

        Assert.Empty(found);
    }

    [Fact]
    public void Inspect_ForAMethodPastMaximumMethodStatements_ReportsItAsAWarningNotAReject()
    {
        var found = Findings(PolicyRule.MethodStatements, """
            class Sample
            {
                void Long()
                {
                    var a = 1; var b = 2; var c = 3; var d = 4; var e = 5;
                    var f = 6; var g = 7; var h = 8; var i = 9; var j = 10;
                    var k = 11;
                }
            }
            """);

        var finding = Assert.Single(found);

        Assert.Equal("11 statements", finding.Measured);
        Assert.Equal("10", finding.Allowed);
        Assert.Equal(PolicyAction.Warn, finding.Action);
    }

    [Fact]
    public void Inspect_ForATypeNameEndingInAMeaninglessSuffix_NamesTheSuffix()
    {
        var found = Findings(PolicyRule.MeaninglessSuffix, "class OrderManager { }");

        Assert.Contains("'OrderManager' ends with 'Manager'", Assert.Single(found).Measured, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_ForAnInterfaceWithoutTheIPrefix_ReportsNaming()
    {
        var found = Findings(PolicyRule.Naming, "interface Repository { }");

        var finding = Assert.Single(found);

        Assert.Contains("interface name 'Repository'", finding.Measured, StringComparison.Ordinal);
        Assert.Contains(NamingDefaults.Expressions[NamingKind.Interface], finding.Allowed, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_ForAConformingInterface_ReportsNothing() =>
        Assert.Empty(Findings(PolicyRule.Naming, "interface IRepository { }"));

    [Fact]
    public void Inspect_ForAnAsyncVoidMethod_ReportsIt()
    {
        var found = Findings(PolicyRule.AsyncVoid, "class Sample { async void Fire() { } }");

        Assert.Equal("async void method", Assert.Single(found).Measured);
    }

    [Fact]
    public void Inspect_ForAnAsyncTaskMethod_ReportsNothing() =>
        Assert.Empty(Findings(PolicyRule.AsyncVoid, "class Sample { async System.Threading.Tasks.Task Fire() { } }"));

    [Fact]
    public void Inspect_ForAConstructorPastMaximumConstructorDependencies_ReportsIt()
    {
        var found = Findings(PolicyRule.ConstructorDependencies, "class Sample { Sample(int a, int b, int c, int d, int e, int f) { } }");

        Assert.Equal("6 constructor dependencies", Assert.Single(found).Measured);
    }

    [Fact]
    public void Inspect_ForAPrimaryConstructorPastTheLimit_ReportsItToo()
    {
        var found = Findings(PolicyRule.ConstructorDependencies, "class Sample(int a, int b, int c, int d, int e, int f) { }");

        Assert.Equal("6 constructor dependencies", Assert.Single(found).Measured);
    }

    [Fact]
    public void Inspect_ForAMethodNameShorterThanTheMeaningfulMinimum_ReportsIt()
    {
        var found = Findings(PolicyRule.MethodNameLength, "class Sample { void Go() { } }");

        var finding = Assert.Single(found);

        Assert.Contains("'Go' is 2 characters", finding.Measured, StringComparison.Ordinal);
        Assert.Equal("a minimum of 3", finding.Allowed);
    }

    [Fact]
    public void Inspect_ForATypeWithMoreMethodsThanMaximumMethodsInClass_ReportsIt()
    {
        var members = string.Concat(Enumerable.Repeat("void Member() { } ", 11));
        var found = Findings(PolicyRule.TypeMethods, "class Sample { " + members + "}");

        Assert.Equal("11 methods", Assert.Single(found).Measured);
    }

    [Fact]
    public void Inspect_ForAMethodPastTheParameterCount_ReportsItAsAWarning()
    {
        var found = Findings(PolicyRule.ParameterCount, "class Sample { void Take(int a, int b, int c, int d, int e, int f) { } }");

        var finding = Assert.Single(found);

        Assert.Equal("6 parameters", finding.Measured);
        Assert.Equal(PolicyAction.Warn, finding.Action);
    }

    [Fact]
    public void Inspect_ForAConditionWithTooManyOperands_ReportsItAsAWarning()
    {
        var found = Findings(PolicyRule.ComplexCondition, "class Sample { bool Test(bool a, bool b, bool c, bool d) => a && b && c && d; }");

        var finding = Assert.Single(found);

        Assert.Equal("4 condition operands", finding.Measured);
        Assert.Equal(PolicyAction.Warn, finding.Action);
    }

    [Fact]
    public void Inspect_ForARenderedFinding_NamesTheRuleIdPathAndDeclaration()
    {
        var finding = Assert.Single(Findings(PolicyRule.AsyncVoid, "class Sample { async void Fire() { } }"));

        Assert.StartsWith("TERSE108", finding.Render(), StringComparison.Ordinal);
        Assert.Contains("Sample.cs:1", finding.Render(), StringComparison.Ordinal);
        Assert.Contains("Sample.Fire", finding.Render(), StringComparison.Ordinal);
        Assert.Contains("fix: ", finding.Explain(), StringComparison.Ordinal);
    }

    private static IReadOnlyList<PolicyFinding> Findings(PolicyRule rule, string source) =>
        PolicyService.Inspect(Root(source), "Sample.cs", Only(rule));

    private static SyntaxNode Root(string source) => CSharpSyntaxTree.ParseText(source).GetRoot();

    private static PolicyOptions Only(PolicyRule rule) => PolicyOptions.Defaults with
    {
        Rules = PolicyRules.All.ToFrozenDictionary(
            info => info.Rule,
            info => new PolicyLimit(info.Rule == rule ? info.Action : PolicyAction.Off, info.Default)),
    };

    [Fact]
    public async Task Inspect_ForThePolicyFixturesBaseline_FindsNothingUnderEveryDefaultRule()
    {
        var path = Path.Combine(Fixtures.RepositoryRoot, "fixtures", "PolicySolution", "src", "Fixture.Policy", "Ledger.cs");
        var source = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        var found = PolicyService.Inspect(Root(source), "Ledger.cs", PolicyOptions.Defaults);

        Assert.Empty(found.Select(finding => finding.Render()));
    }

    [Fact]
    public void Inspect_ForAFlatElseIfLadder_DoesNotCountEachLinkAsANestingLevel() =>
            Assert.Empty(Findings(PolicyRule.NestingDepth, """
            class Sample
            {
                int Pick(int value)
                {
                    if (value == 1) { return 1; }
                    else if (value == 2) { return 2; }
                    else if (value == 3) { return 3; }
                    else { return 0; }
                }
            }
            """));

    [Fact]
    public void Inspect_ForGenuinelyNestedBlocks_StillReportsNestingDepth()
    {
        var found = Findings(PolicyRule.NestingDepth, """
            class Sample
            {
                void Deep(bool a, bool b, bool c, bool d, bool e)
                {
                    if (a) { if (b) { if (c) { if (d) { if (e) { } } } } }
                }
            }
            """);

        Assert.Equal("5 nesting levels", Assert.Single(found).Measured);
    }

    [Fact]
    public void Inspect_ForAnEventField_ChecksItAgainstTheEventPatternNotTheLocalOne() =>
        Assert.Empty(Findings(PolicyRule.Naming, "class Sample { public event System.EventHandler Changed; }"));

    [Fact]
    public void Inspect_ForAStaticReadonlyFieldInPascalCase_IsNotANamingViolation() =>
        Assert.Empty(Findings(PolicyRule.Naming, "class Sample { private static readonly int Entries = 1; }"));

    [Fact]
    public void Inspect_ForAnInstanceFieldInPascalCase_IsStillANamingViolation()
    {
        var found = Findings(PolicyRule.Naming, "class Sample { private int Entries = 1; }");

        Assert.Contains("field name 'Entries'", Assert.Single(found).Measured, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_ForAParenthesisedCondition_ReportsItOnceNotTwice()
    {
        var found = Findings(PolicyRule.ComplexCondition, "class Sample { bool Test(bool a, bool b, bool c, bool d, bool e) => (a && b && c && d) && e; }");

        Assert.Equal("5 condition operands", Assert.Single(found).Measured);
    }

    [Fact]
    public void Key_ForTheSameDeclaration_IgnoresTheMeasuredValueSoAnImprovementIsNotCountedAsIntroduced()
    {
        var worse = Assert.Single(Findings(PolicyRule.MethodStatements, Statements(14)));
        var better = Assert.Single(Findings(PolicyRule.MethodStatements, Statements(12)));

        Assert.NotEqual(worse.Measured, better.Measured);
        Assert.Equal(worse.Key, better.Key);
    }

    private static string Statements(int count) =>
        "class Sample { void Long() {" + string.Concat(Enumerable.Range(0, count).Select(index => " var a" + index.ToString(CultureInfo.InvariantCulture) + " = 1;")) + " } }";
}
