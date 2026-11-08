using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

internal static class Duplication
{
    public const int MinimumTokens = 40;
    public const double DuplicationPenalty = 0.5;

    public static IEnumerable<CodeSmell> Smells(SyntaxNode root)
    {
        var originals = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var function in root.DescendantNodes().Where(IsProductionFunction))
        {
            if (CodeHealth.Body(function) is not { } body || Shape(body) is not { } shape)
                continue;

            var member = CodeHealth.Member(function);

            if (!originals.TryAdd(shape, member))
                yield return new CodeSmell("Code Duplication", member + " (copy of " + originals[shape] + ")", DuplicationPenalty);
        }
    }

    private static bool IsProductionFunction(SyntaxNode node) =>
        CodeHealth.IsFunction(node) && !(node is MethodDeclarationSyntax method && PolicyService.IsTest(method));

    private static string? Shape(SyntaxNode body)
    {
        var count = body.DescendantTokens().Count();

        return count < MinimumTokens
            ? null
            : string.Create(count, body, static (characters, node) =>
            {
                var index = 0;

                foreach (var token in node.DescendantTokens())
                    characters[index++] = (char)Normalized(token);
            });
    }

    private static int Normalized(SyntaxToken token) => token.Kind() switch
    {
        SyntaxKind.IdentifierToken => (int)SyntaxKind.IdentifierToken,
        SyntaxKind.NumericLiteralToken or SyntaxKind.StringLiteralToken or SyntaxKind.CharacterLiteralToken
            or SyntaxKind.InterpolatedStringTextToken or SyntaxKind.SingleLineRawStringLiteralToken
            or SyntaxKind.MultiLineRawStringLiteralToken or SyntaxKind.Utf8StringLiteralToken => (int)SyntaxKind.StringLiteralToken,
        _ => token.RawKind,
    };
}
