using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerseSharp.Core;

public static class SymbolSearch
{
    public static async Task<IReadOnlyList<ISymbol>> FindAsync(
        LoadedWorkspace workspace,
        string query,
        string? kind,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var found = new List<ISymbol>(maxResults);

        foreach (var project in workspace.Solution.Projects)
        {
            if (found.Count >= maxResults)
                break;

            var matches = await SymbolFinder
                .FindSourceDeclarationsWithPatternAsync(project, query, cancellationToken)
                .ConfigureAwait(false);

            found.AddRange(matches.Where(symbol => KindMatches(symbol, kind)));
        }

        return Rank(found, maxResults);
    }

    private static ISymbol[] Rank(List<ISymbol> found, int maxResults) =>
        [.. found
            .DistinctBy(symbol => SymbolId.From(symbol).Value, StringComparer.Ordinal)
            .OrderByDescending(symbol => symbol.DeclaredAccessibility is Accessibility.Public)
            .ThenBy(symbol => symbol.Name.Length)
            .Take(maxResults)];

    private static bool KindMatches(ISymbol symbol, string? kind) =>
        string.IsNullOrWhiteSpace(kind)
        || SymbolFormat.Kind(symbol).Equals(kind, StringComparison.OrdinalIgnoreCase)
        || symbol.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase);
}
