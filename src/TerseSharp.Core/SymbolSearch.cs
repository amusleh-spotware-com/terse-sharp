using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerseSharp.Core;

public readonly record struct SymbolMatches(IReadOnlyList<ISymbol> Ranked, int Total, bool TotalIsExact);

public static class SymbolSearch
{
    public static async Task<SymbolMatches> FindAsync(
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

        return Summarize(perProject, ceiling, maxResults);
    }

    private static SymbolMatches Summarize(ISymbol[][] perProject, int ceiling, int maxResults)
    {
        var declarations = MatchCount(perProject);
        var window = Distinct(perProject.SelectMany(found => found).Take(ceiling));
        var exact = declarations <= ceiling;

        return new SymbolMatches(Rank(window, maxResults), exact ? window.Length : declarations, exact);
    }

    private static int MatchCount(ISymbol[][] perProject)
    {
        var declarations = 0;

        foreach (var project in perProject)
            declarations += project.Length;

        return declarations;
    }

    private static Identified[] Distinct(IEnumerable<ISymbol> found) =>
        [.. found
            .Select(symbol => new Identified(symbol, SymbolId.From(symbol).Value))
            .DistinctBy(entry => entry.Id, StringComparer.Ordinal)];

    private static ISymbol[] Rank(Identified[] distinct, int maxResults) => [.. distinct
    .OrderByDescending(entry => entry.Symbol.DeclaredAccessibility is Accessibility.Public)
    .ThenBy(entry => entry.Symbol.Name.Length)
    .ThenBy(entry => entry.Id, StringComparer.Ordinal)
    .Take(ResultCap.Shown(distinct.Length, maxResults))
    .Select(entry => entry.Symbol)];
    private static bool KindMatches(ISymbol symbol, string? kind) =>
        string.IsNullOrWhiteSpace(kind)
        || SymbolFormat.Kind(symbol).Equals(kind, StringComparison.OrdinalIgnoreCase)
        || symbol.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase);

    private readonly record struct Identified(ISymbol Symbol, string Id);
}
