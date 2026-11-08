using System.Globalization;
using Microsoft.CodeAnalysis.CSharp;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class CodeHealthTests
{
    [Fact]
    public void Assess_ForSmallFlatCode_ScoresAPerfectTen()
    {
        var health = Assess("class Ledger { int Total(int amount) => amount + 1; }");

        Assert.Equal(10.0, health.Score);
        Assert.Empty(health.Smells);
    }

    [Fact]
    public void Assess_ForFourNestedConditionals_ChargesDeepNestedComplexityOnly()
    {
        var health = Assess("class Sample { void Deep(bool a, bool b, bool c, bool d) { if (a) { if (b) { if (c) { if (d) { } } } } } }");

        Assert.Equal(9.3, health.Score);
        Assert.Equal(["Deep Nested Complexity"], Names(health));
    }

    [Fact]
    public void Assess_ForThreeNestedConditionals_ChargesNothing() =>
        Assert.Equal(10.0, Assess("class Sample { void Deep(bool a, bool b, bool c) { if (a) { if (b) { if (c) { } } } } }").Score);

    [Fact]
    public void Assess_ForTenBranches_ChargesComplexMethod()
    {
        var health = Assess("class Sample { int Pick(int v) => v switch { 1 => 1, 2 => 2, 3 => 3, 4 => 4, 5 => 5, 6 => 6, 7 => 7, 8 => 8, 9 => 9, _ => 0 }; }");

        Assert.Equal(9.5, health.Score);
        Assert.Equal(["Complex Method"], Names(health));
    }

    [Fact]
    public void Assess_ForNineBranches_ChargesNothing() =>
        Assert.Equal(10.0, Assess("class Sample { int Pick(int v) => v switch { 1 => 1, 2 => 2, 3 => 3, 4 => 4, 5 => 5, 6 => 6, 7 => 7, 8 => 8, _ => 0 }; }").Score);

    [Fact]
    public void Assess_ForALongComplexMethod_ChargesBrainMethodComplexMethodAndLargeMethod()
    {
        var health = Assess(BrainMethod());

        Assert.Equal(7.5, health.Score);
        Assert.Equal(["Brain Method", "Complex Method", "Large Method"], Names(health));
        Assert.Equal("code health 7.5 (Brain Method Sample.Brain, Complex Method Sample.Brain, Large Method Sample.Brain)", health.Describe());
    }

    [Fact]
    public void Assess_ForTwoSeparateChunksOfNestedLogic_ChargesBumpyRoad()
    {
        var health = Assess("class Sample { void Bumpy(bool a, bool b) { if (a) { if (b) { } } if (b) { if (a) { } } } }");

        Assert.Equal(9.5, health.Score);
        Assert.Equal(["Bumpy Road"], Names(health));
    }

    [Fact]
    public void Assess_ForFiveParameters_ChargesExcessFunctionArguments()
    {
        var health = Assess("class Sample { void Wide(int a, int b, int c, int d, int e) { } }");

        Assert.Equal(9.8, health.Score);
        Assert.Equal(["Excess Function Arguments"], Names(health));
    }

    [Fact]
    public void Assess_ForAConditionJoiningThreeOperands_ChargesComplexConditional()
    {
        var health = Assess("class Sample { bool Check(bool a, bool b, bool c) { if (a && b || c) return true; return false; } }");

        Assert.Equal(9.8, health.Score);
        Assert.Equal(["Complex Conditional"], Names(health));
    }

    [Fact]
    public void Assess_ForAFileOverAThousandLines_ChargesLargeFileAndLandsExactlyOnNine()
    {
        var fields = string.Join('\n', Enumerable.Range(0, 1001).Select(index => string.Create(CultureInfo.InvariantCulture, $"int field{index};")));

        var health = Assess("class Sample\n{\n" + fields + "\n}");

        Assert.Equal(9.0, health.Score);
        Assert.Equal(["Large File"], Names(health));
    }

    [Fact]
    public void Assess_ForMoreSmellsThanTheScaleHolds_StopsAtTheFloorOfOne()
    {
        var methods = string.Join('\n', Enumerable.Range(0, 20).Select(index => string.Create(
            CultureInfo.InvariantCulture,
            $"void Deep{index}(bool a, bool b, bool c, bool d) {{ if (a) {{ if (b) {{ if (c) {{ if (d) {{ }} }} }} }} }}")));

        var health = Assess("class Sample {\n" + methods + "\n}");

        Assert.Equal(1.0, health.Score);
        Assert.EndsWith(", +17 more)", health.Describe(), StringComparison.Ordinal);
    }

    internal static string BrainMethod()
    {
        var branches = Enumerable.Range(1, 9).Select(index => string.Create(CultureInfo.InvariantCulture, $"        if (v == {index}) total += {index};"));
        var statements = Enumerable.Range(0, 70).Select(index => string.Create(CultureInfo.InvariantCulture, $"        total += v * {index};"));

        return "class Sample\n{\n    int Brain(int v)\n    {\n        var total = 0;\n"
            + string.Join('\n', branches.Concat(statements))
            + "\n        return total;\n    }\n}\n";
    }

    private static CodeHealthScore Assess(string source) => CodeHealth.Assess(CSharpSyntaxTree.ParseText(source).GetRoot());

    private static string[] Names(CodeHealthScore health) => [.. health.Smells.Select(smell => smell.Name)];

    [Fact]
    public void Assess_ForATypeWithThreeUnrelatedResponsibilities_ChargesLowCohesion()
    {
        var health = Assess("""
        class Mixed
        {
            private int count;
            private string name = "";
            private double rate;
            void Bump() { count++; }
            void Rename(string value) { name = value; }
            void Scale(double factor) { rate *= factor; }
        }
        """);

        Assert.Equal(9.0, health.Score);
        Assert.Equal("Low Cohesion Mixed", Assert.Single(health.Smells).Name + " " + health.Smells[0].Member);
    }

    [Fact]
    public void Assess_ForMembersJoinedByCallsAndSharedFields_ChargesNothing() =>
        Assert.Equal(10.0, Assess("""
        class Joined
        {
            private int count;
            private string name = "";
            private double rate;
            void Bump() { count++; Rename("x"); }
            void Rename(string value) { name = value; this.Scale(2); }
            void Scale(double factor) { rate *= factor; }
        }
        """).Score);

    [Fact]
    public void Assess_ForStaticHelpersAndTestMethods_NeverChargesLowCohesion() =>
        Assert.Equal(10.0, Assess("""
        static class Helpers
        {
            static int One() => 1;
            static int Two() => 2;
            static int Three() => 3;
        }
        sealed class Suite
        {
            [Fact] public void A() { }
            [Fact] public void B() { }
            [Theory] public void C() { }
        }
        """).Score);

    [Fact]
    public void Assess_ForTwoMethodsThatDifferOnlyInNamesAndLiterals_ChargesCodeDuplicationOnTheCopy()
    {
        var health = Assess("""
        class Sample
        {
            int First(int value) { var total = value + 1; if (total > 10) { total = total * 2; } total += value * 4; return total - value + Helper(total, 3); }
            int Second(int amount) { var sum = amount + 7; if (sum > 99) { sum = sum * 5; } sum += amount * 8; return sum - amount + Other(sum, 1); }
        }
        """);

        Assert.Equal(9.5, health.Score);
        Assert.Equal("Code Duplication Sample.Second (copy of Sample.First)", Assert.Single(health.Smells).Name + " " + health.Smells[0].Member);
    }

    [Fact]
    public void Assess_ForTwoIdenticalBodiesUnderTheTokenFloor_ChargesNothing() =>
        Assert.Equal(10.0, Assess("class Sample { int First(int value) => value + 1; int Second(int amount) => amount + 2; }").Score);

    [Fact]
    public void Assess_ForAPartialTypeWhoseJoiningMemberMayLiveInAnotherPart_NeverChargesLowCohesion() =>
        Assert.Equal(10.0, Assess("""
        partial class Split
        {
            private int count;
            private string name = "";
            private double rate;
            void Bump() { count++; }
            void Rename(string value) { name = value; }
            void Scale(double factor) { rate *= factor; }
        }
        """).Score);

    [Fact]
    public void Assess_ForOverridesThatUseOnlyTheirParameters_ChargesNothing() =>
        Assert.Equal(10.0, Assess("""
        sealed class Converter : Base
        {
            public override bool CanConvert(System.Type type) => type == typeof(int);
            public override int Read(string text) => text.Length;
            public override string Write(int value) => value.ToString();
        }
        """).Score);

    [Fact]
    public void Assess_ForThreeFieldKeywordProperties_ChargesNothing() =>
        Assert.Equal(10.0, Assess("""
        sealed class Person
        {
            public string First { get; set => field = value.Trim(); }
            public string Last { get; set => field = value.Trim(); }
            public string Title { get; set => field = value.Trim(); }
        }
        """).Score);

    [Fact]
    public void Assess_ForMethodsJoinedByAFieldLikeEvent_ChargesNothing() =>
        Assert.Equal(10.0, Assess("""
        sealed class Feed
        {
            public event System.EventHandler? Changed;
            private int count;
            private string name = "";
            private double rate;
            void Bump() { count++; Changed?.Invoke(this, System.EventArgs.Empty); }
            void Rename(string value) { name = value; Changed?.Invoke(this, System.EventArgs.Empty); }
            void Scale(double factor) { rate *= factor; Changed?.Invoke(this, System.EventArgs.Empty); }
        }
        """).Score);
}
