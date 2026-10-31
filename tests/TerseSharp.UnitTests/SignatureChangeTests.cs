using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class SignatureChangeTests
{
    [Fact]
    public void Kept_AMultiLineListGainingAParameter_KeepsEveryParameterOnItsOwnLine()
    {
        var method = (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration("void Write(\n        int first,\n        int second)\n    {\n    }")!;

        var kept = method.WithParameterList(ParameterLayout.Kept(method.ParameterList, SyntaxFactory.ParseParameterList("(int first, int second, int third)")));

        Assert.Equal("void Write(\n        int first,\n        int second,\n        int third)\n    {\n    }", kept.ToFullString());
    }

    [Fact]
    public void Kept_ASingleLineList_StaysOnOneLineAndKeepsTheBraceOnItsOwnLine()
    {
        var method = (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration("void Write(int first)\n    {\n    }")!;

        var kept = method.WithParameterList(ParameterLayout.Kept(method.ParameterList, SyntaxFactory.ParseParameterList("(int first,int second)")));

        Assert.Equal("void Write(int first, int second)\n    {\n    }", kept.ToFullString());
    }

    [Fact]
    public void Rearranged_AReorderAndAnAddedValue_MovesEachArgumentByNameAndInsertsTheValue()
    {
        var call = (InvocationExpressionSyntax)SyntaxFactory.ParseExpression("Write(1, 2)");
        var change = new SignatureChange(
            ["first", "second"],
            SyntaxFactory.ParseParameterList("(int second, int first, int third)"),
            new Dictionary<string, ArgumentSyntax>(StringComparer.Ordinal) { ["third"] = SyntaxFactory.Argument(SyntaxFactory.ParseExpression("3")) });

        var rearranged = ArgumentOrder.Rearranged(call.ArgumentList, call.ArgumentList, change, receiver: 0);

        Assert.NotNull(rearranged);
        Assert.Equal("(2, 1, 3)", rearranged.ToFullString());
    }

    [Fact]
    public void Rearranged_AParameterNoCallerSupplies_NamesEveryArgumentAfterIt()
    {
        var call = (InvocationExpressionSyntax)SyntaxFactory.ParseExpression("Write(1, 2)");
        var change = new SignatureChange(
            ["first", "second"],
            SyntaxFactory.ParseParameterList("(int first, int skipped = 0, int second = 0)"),
            new Dictionary<string, ArgumentSyntax>(StringComparer.Ordinal));

        var rearranged = ArgumentOrder.Rearranged(call.ArgumentList, call.ArgumentList, change, receiver: 0);

        Assert.NotNull(rearranged);
        Assert.Equal("(1, second: 2)", rearranged.ToFullString());
    }

    [Fact]
    public void Rearranged_AnExpandedParamsCall_CannotBePlacedByName()
    {
        var call = (InvocationExpressionSyntax)SyntaxFactory.ParseExpression("Write(1, 2, 3)");
        var change = new SignatureChange(
            ["values"],
            SyntaxFactory.ParseParameterList("(int extra, params int[] values)"),
            new Dictionary<string, ArgumentSyntax>(StringComparer.Ordinal) { ["extra"] = SyntaxFactory.Argument(SyntaxFactory.ParseExpression("0")) });

        Assert.Null(ArgumentOrder.Rearranged(call.ArgumentList, call.ArgumentList, change, receiver: 0));
    }

    [Fact]
    public void Rearranged_AListWhoseNamesDidNotMove_ReturnsTheSameNode()
    {
        var call = (InvocationExpressionSyntax)SyntaxFactory.ParseExpression("Write(1, 2)");
        var change = new SignatureChange(
            ["first", "second"],
            SyntaxFactory.ParseParameterList("(long first, long second)"),
            new Dictionary<string, ArgumentSyntax>(StringComparer.Ordinal));

        var rearranged = ArgumentOrder.Rearranged(call.ArgumentList, call.ArgumentList, change, receiver: 0);

        Assert.Same(call.ArgumentList, rearranged);
    }

    [Fact]
    public void Rearranged_ARenamedParameterWithNoValue_CannotBePlacedByName()
    {
        var call = (InvocationExpressionSyntax)SyntaxFactory.ParseExpression("Retry(5)");
        var change = new SignatureChange(
            ["attempts"],
            SyntaxFactory.ParseParameterList("(int count = 3)"),
            new Dictionary<string, ArgumentSyntax>(StringComparer.Ordinal));

        Assert.Null(ArgumentOrder.Rearranged(call.ArgumentList, call.ArgumentList, change, receiver: 0));
    }

    [Fact]
    public void Rearranged_ARemovedParameter_DropsOnlyItsArgument()
    {
        var call = (InvocationExpressionSyntax)SyntaxFactory.ParseExpression("Write(1, 2)");
        var change = new SignatureChange(
            ["first", "second"],
            SyntaxFactory.ParseParameterList("(int first)"),
            new Dictionary<string, ArgumentSyntax>(StringComparer.Ordinal));

        var rearranged = ArgumentOrder.Rearranged(call.ArgumentList, call.ArgumentList, change, receiver: 0);

        Assert.NotNull(rearranged);
        Assert.Equal("(1)", rearranged.ToFullString());
    }

    [Fact]
    public void Rearranged_ARemovedParameterBesideAnOmittedOptional_DropsOnlyItsArgument()
    {
        var call = (InvocationExpressionSyntax)SyntaxFactory.ParseExpression("Write(1, 2)");
        var change = new SignatureChange(
            ["first", "second", "third"],
            SyntaxFactory.ParseParameterList("(int first, int third = 0)"),
            new Dictionary<string, ArgumentSyntax>(StringComparer.Ordinal));

        var rearranged = ArgumentOrder.Rearranged(call.ArgumentList, call.ArgumentList, change, receiver: 0);

        Assert.NotNull(rearranged);
        Assert.Equal("(1)", rearranged.ToFullString());
    }

    [Theory]
    [InlineData("/// <see cref=\"M(int)\"/>", false)]
    [InlineData("nameof(M)", false)]
    [InlineData("M(1)", true)]
    public void BindsArguments_ForANameofOrACrefReference_IsFalseAndForACallIsTrue(string marker, bool expected)
    {
        var text = "class C\n{\n    /// <see cref=\"M(int)\"/>\n    void M(int a) { }\n    string N() => nameof(M);\n    void P() => M(1);\n}\n";
        var tree = CSharpSyntaxTree.ParseText(text, cancellationToken: TestContext.Current.CancellationToken);
        var start = text.IndexOf(marker, StringComparison.Ordinal) + marker.IndexOf('M', StringComparison.Ordinal);

        Assert.Equal(expected, ReferenceSites.BindsArguments(Location.Create(tree, new Microsoft.CodeAnalysis.Text.TextSpan(start, 1))));
    }
}
