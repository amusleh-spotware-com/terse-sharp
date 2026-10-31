using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public static class ParameterLayout
{
    public static ParameterListSyntax Kept(ParameterListSyntax original, ParameterListSyntax replacement)
    {
        var parameters = new ParameterSyntax[replacement.Parameters.Count];

        for (var index = 0; index < parameters.Length; index++)
            parameters[index] = replacement.Parameters[index].WithoutTrivia().WithLeadingTrivia(Leading(original.Parameters, index));

        return original.WithParameters(SyntaxFactory.SeparatedList(parameters, Commas(original.Parameters, parameters.Length)));
    }

    public static SyntaxToken[] Commas<TNode>(SeparatedSyntaxList<TNode> original, int count)
        where TNode : SyntaxNode
    {
        var commas = new SyntaxToken[Math.Max(0, count - 1)];

        for (var index = 0; index < commas.Length; index++)
            commas[index] = SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(CommaTrivia(original, index));

        return commas;
    }

    private static SyntaxTriviaList Leading(SeparatedSyntaxList<ParameterSyntax> original, int index) =>
        original.Count == 0 ? default : original[Math.Min(index, original.Count - 1)].GetLeadingTrivia();

    private static SyntaxTriviaList CommaTrivia<TNode>(SeparatedSyntaxList<TNode> original, int index)
        where TNode : SyntaxNode =>
        original.SeparatorCount == 0
            ? SyntaxFactory.TriviaList(SyntaxFactory.Space)
            : original.GetSeparator(Math.Min(index, original.SeparatorCount - 1)).TrailingTrivia;
}
