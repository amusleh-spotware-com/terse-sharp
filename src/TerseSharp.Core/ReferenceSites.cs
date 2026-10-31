using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerseSharp.Core;

public static class ReferenceSites
{
    public static bool Moves(SignatureChange change) =>
        !change.OldNames.SequenceEqual(change.Parameters.Parameters.Select(parameter => parameter.Identifier.ValueText), StringComparer.Ordinal);

    public static ILookup<DocumentId, ReferenceLocation> Direct(IEnumerable<ReferencedSymbol> found, IMethodSymbol method) =>
        found
            .Where(symbol => SymbolEqualityComparer.Default.Equals(symbol.Definition.OriginalDefinition, method.OriginalDefinition))
            .SelectMany(symbol => symbol.Locations)
            .Where(location => !location.IsImplicit)
            .ToLookup(location => location.Document.Id);

    public static List<Location> Cascaded(IEnumerable<ReferencedSymbol> found, IMethodSymbol method) =>
    [
        .. found
            .Where(symbol => !SymbolEqualityComparer.Default.Equals(symbol.Definition.OriginalDefinition, method.OriginalDefinition))
            .SelectMany(symbol => symbol.Locations)
            .Where(location => !location.IsImplicit && BindsArguments(location.Location))
            .Select(location => location.Location),
    ];

    public static bool BindsArguments(Location location) =>
        TokenAt(location) is not { Parent: { } parent } token || !(token.IsPartOfStructuredTrivia() || parent.AncestorsAndSelf().Any(IsNameOf));

    private static bool IsNameOf(SyntaxNode node) =>
        node is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } };

    private static SyntaxToken? TokenAt(Location location) =>
        location.SourceTree is { } tree && tree.TryGetRoot(out var root) ? root.FindToken(location.SourceSpan.Start, findInsideTrivia: true) : null;
}
