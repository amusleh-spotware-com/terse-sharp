using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class CognitiveComplexityTests
{
    [Fact]
    public void Of_ForThePluginsNestedExample_Scores9()
    {
        var score = Score("""
            class Sample
            {
                void MyMethod(bool one, bool two)
                {
                    try
                    {
                        if (one)
                        {
                            for (var index = 0; index < 10; index++)
                            {
                                while (two) { }
                            }
                        }
                    }
                    catch (System.Exception)
                    {
                        if (two) { }
                    }
                }
            }
            """);

        Assert.Equal(9, score);
    }

    [Fact]
    public void Of_ForAnElseIfChain_ChargesOneFlatPerLinkWithNoNesting()
    {
        var score = Score("""
            class Sample
            {
                void MyMethod(int value)
                {
                    if (value == 1) { }
                    else if (value == 2) { }
                    else { }
                }
            }
            """);

        Assert.Equal(3, score);
    }

    [Fact]
    public void Of_ForARunOfTheSameOperator_ChargesOncePerRunNotPerOperator()
    {
        var score = Score("""
            class Sample
            {
                bool MyMethod(bool a, bool b, bool c)
                {
                    if (a && b && c) return true;
                    return a && b || c;
                }
            }
            """);

        Assert.Equal(4, score);
    }

    [Fact]
    public void Of_ForALambda_AddsNestingWithoutAddingComplexityOfItsOwn()
    {
        var score = Score("""
            class Sample
            {
                void MyMethod(System.Collections.Generic.List<int> items)
                {
                    items.ForEach(item =>
                    {
                        if (item > 0) { }
                    });
                }
            }
            """);

        Assert.Equal(2, score);
    }

    [Fact]
    public void Of_ForSwitchExpressionsTernariesPatternsAndContinue_ChargesOnlyTheForeach()
    {
        var score = Score("""
            class Sample
            {
                int MyMethod(int value, int? fallback)
                {
                    var mapped = value switch { 1 => 1, _ => 2 };
                    var ranged = value is > 0 and < 10 ? 1 : 2;

                    foreach (var _ in System.Array.Empty<int>())
                        continue;

                    return mapped + ranged + (fallback ?? 0);
                }
            }
            """);

        Assert.Equal(1, score);
    }

    [Fact]
    public void Of_ForASwitchStatement_ChargesTheSwitchAndEveryBreak()
    {
        var score = Score("""
            class Sample
            {
                void MyMethod(int value)
                {
                    switch (value)
                    {
                        case 1: break;
                        default: break;
                    }
                }
            }
            """);

        Assert.Equal(3, score);
    }

    [Fact]
    public void Of_ForARecursiveCall_ChargesOne()
    {
        var score = Score("""
            class Sample
            {
                int MyMethod(int value) => value <= 1 ? 1 : MyMethod(value - 1);
            }
            """);

        Assert.Equal(1, score);
    }

    [Fact]
    public void Of_ForAnExpressionBodiedMemberWithNoBranching_ScoresZero()
    {
        var score = Score("""
            class Sample
            {
                int MyMethod(int value) => value + 1;
            }
            """);

        Assert.Equal(0, score);
    }

    private static int Score(string source)
    {
        var method = CSharpSyntaxTree.ParseText(source)
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First();

        return CognitiveComplexity.Of(method);
    }
}
