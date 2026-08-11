using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerseSharp.Core;

public readonly record struct SymbolMatches(IReadOnlyList<ISymbol> Ranked, int Total, bool TotalIsExact, int Withheld);

public static class SymbolSearch
{
    public static async Task<SymbolMatches> FindAsync(
    LoadedWorkspace workspace,
    string query,
    string? kind,
    string? scope,
    int maxResults,
    CancellationToken cancellationToken,
    bool foldTests = false)
    {
        var ceiling = Math.Max(Math.Min(maxResults, 1024) * 8, 256);
        var projects = Scoped(workspace, scope);
        var perProject = new Identified[projects.Length][];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, projects.Length),
            ParallelWork.Options(cancellationToken),
            async (index, token) =>
            {
                var matches = await SymbolFinder
                    .FindSourceDeclarationsWithPatternAsync(projects[index], query, token)
                    .ConfigureAwait(false);

                var test = TestScope.Of(projects[index]) is "test";

                perProject[index] = [.. matches
                .Where(candidate => KindMatches(candidate, kind))
                .Select(candidate => new Identified(candidate, SymbolId.From(candidate).Value, test))];
            }).ConfigureAwait(false);

        return Summarize(perProject, ceiling, maxResults, foldTests && scope is not { Length: > 0 });
    }

    private static Project[] Scoped(LoadedWorkspace workspace, string? scope) => scope is { Length: > 0 } wanted
        ? [.. workspace.Solution.Projects.Where(project => string.Equals(TestScope.Of(project), wanted, StringComparison.Ordinal))]
        : [.. workspace.Solution.Projects];

    private static SymbolMatches Summarize(Identified[][] perProject, int ceiling, int maxResults, bool foldTests)
    {
        var declarations = MatchCount(perProject);
        var window = Distinct(perProject.SelectMany(found => found).Take(ceiling));
        var exact = declarations <= ceiling;
        var kept = foldTests && exact ? [.. window.Where(entry => !entry.IsTest)] : window;
        var production = kept.Length is 0 ? window : kept;

        return production.Length == window.Length
            ? new SymbolMatches(Rank(window, maxResults), exact ? window.Length : declarations, exact, 0)
            : new SymbolMatches(Rank(production, maxResults), production.Length, exact, window.Length - production.Length);
    }

    private static int MatchCount(Identified[][] perProject)
    {
        var declarations = 0;

        foreach (var project in perProject)
            declarations += project.Length;

        return declarations;
    }

    private static Identified[] Distinct(IEnumerable<Identified> found) =>
    [.. found.DistinctBy(entry => entry.Id, StringComparer.Ordinal)];

    private static ISymbol[] Rank(Identified[] distinct, int maxResults) => [.. distinct
    .OrderBy(entry => entry.IsTest)
    .ThenByDescending(entry => entry.Symbol.DeclaredAccessibility is Accessibility.Public)
    .ThenBy(entry => entry.Symbol.Name.Length)
    .ThenBy(entry => entry.Id, StringComparer.Ordinal)
    .Take(ResultCap.Shown(distinct.Length, maxResults))
    .Select(entry => entry.Symbol)];
    private static bool KindMatches(ISymbol symbol, string? kind) =>
        string.IsNullOrWhiteSpace(kind)
        || SymbolFormat.Kind(symbol).Equals(kind, StringComparison.OrdinalIgnoreCase)
        || symbol.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase);

    private readonly record struct Identified(ISymbol Symbol, string Id, bool IsTest);
}
