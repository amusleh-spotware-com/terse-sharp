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

    public static async Task<List<Location>> CascadedAsync(IEnumerable<ReferencedSymbol> found, IMethodSymbol method, CancellationToken cancellationToken)
    {
        var cascaded = new List<Location>();

        foreach (var reference in Cascading(found, method))
        {
            if (await BindsArgumentsAsync(reference.Location, cancellationToken).ConfigureAwait(false))
                cascaded.Add(reference.Location);
        }

        return cascaded;
    }

    public static async Task<bool> BindsArgumentsAsync(Location location, CancellationToken cancellationToken) =>
        location.SourceTree is not { } tree || BindsArguments(await tree.GetRootAsync(cancellationToken).ConfigureAwait(false), location);

    public static bool BindsArguments(SyntaxNode root, Location location) =>
        root.FindToken(location.SourceSpan.Start, findInsideTrivia: true) is not { Parent: { } parent } token
        || !(token.IsPartOfStructuredTrivia() || parent.AncestorsAndSelf().Any(IsNameOf));

    private static IEnumerable<ReferenceLocation> Cascading(IEnumerable<ReferencedSymbol> found, IMethodSymbol method) =>
        found
            .Where(symbol => !SymbolEqualityComparer.Default.Equals(symbol.Definition.OriginalDefinition, method.OriginalDefinition))
            .SelectMany(symbol => symbol.Locations)
            .Where(location => !location.IsImplicit);

    private static bool IsNameOf(SyntaxNode node) =>
        node is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } };
}
