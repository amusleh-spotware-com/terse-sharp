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
        var ceiling = Math.Max(Math.Min(maxResults, 1024) * 8, 256);
        var projects = workspace.Solution.Projects.ToArray();
        var perProject = new ISymbol[projects.Length][];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, projects.Length),
            ParallelWork.Options(cancellationToken),
            async (index, token) =>
            {
                var matches = await SymbolFinder
                    .FindSourceDeclarationsWithPatternAsync(projects[index], query, token)
                    .ConfigureAwait(false);

                perProject[index] = [.. matches.Where(candidate => KindMatches(candidate, kind))];
            }).ConfigureAwait(false);

        return Rank(perProject.SelectMany(found => found).Take(ceiling), maxResults);
    }

    private static ISymbol[] Rank(IEnumerable<ISymbol> found, int maxResults) =>
        [.. found
            .DistinctBy(symbol => SymbolId.From(symbol).Value, StringComparer.Ordinal)
            .OrderByDescending(symbol => symbol.DeclaredAccessibility is Accessibility.Public)
            .ThenBy(symbol => symbol.Name.Length)
            .ThenBy(symbol => SymbolId.From(symbol).Value, StringComparer.Ordinal)
            .Take(maxResults)];

    private static bool KindMatches(ISymbol symbol, string? kind) =>
        string.IsNullOrWhiteSpace(kind)
        || SymbolFormat.Kind(symbol).Equals(kind, StringComparison.OrdinalIgnoreCase)
        || symbol.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase);
}
