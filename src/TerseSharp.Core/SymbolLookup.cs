using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerseSharp.Core;

public static class SymbolLookup
{
    public static async Task<Result<ISymbol>> ResolveAsync(
        LoadedWorkspace workspace,
        string symbolId,
        CancellationToken cancellationToken)
    {
        var matches = await FindAllAsync(workspace, symbolId, cancellationToken).ConfigureAwait(false);
        var distinct = matches.DistinctBy(Describe, StringComparer.Ordinal).ToArray();

        if (distinct.Length is 1)
            return Result.Ok(distinct[0]);

        if (distinct.Length > 1)
            return Result.Fail<ISymbol>(Errors.AmbiguousSymbol(symbolId, [.. distinct.Select(Describe)]));

        var nearest = await NearestAsync(workspace, symbolId, cancellationToken).ConfigureAwait(false);

        return Result.Fail<ISymbol>(Errors.SymbolNotFound(symbolId, nearest));
    }

    private static string Describe(ISymbol symbol) => string.Create(
        CultureInfo.InvariantCulture,
        $"{symbol.ContainingAssembly?.Name ?? "-"}/{SymbolFormat.Location(symbol)}");

    public static async Task<IReadOnlyList<ISymbol>> FindAllAsync(
        LoadedWorkspace workspace,
        string symbolId,
        CancellationToken cancellationToken)
    {
        var candidates = await CandidatesAsync(workspace, symbolId, cancellationToken).ConfigureAwait(false);
        var perProject = new ISymbol[candidates.Count][];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, candidates.Count),
            ParallelWork.Options(cancellationToken),
            async (index, token) =>
            {
                var compilation = await candidates[index].GetCompilationAsync(token).ConfigureAwait(false);

                perProject[index] = compilation is null
                    ? []
                    : [.. DocumentationCommentId.GetSymbolsForDeclarationId(symbolId, compilation)];
            }).ConfigureAwait(false);

        return [.. perProject.SelectMany(found => found)];
    }

    private static async Task<IReadOnlyList<Project>> CandidatesAsync(
        LoadedWorkspace workspace,
        string symbolId,
        CancellationToken cancellationToken)
    {
        var name = LastSegment(symbolId);
        var projects = workspace.Solution.Projects.ToArray();
        var matched = new bool[projects.Length];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, projects.Length),
            ParallelWork.Options(cancellationToken),
            async (index, token) =>
            {
                var declarations = await SymbolFinder
                    .FindSourceDeclarationsAsync(projects[index], name, ignoreCase: false, token)
                    .ConfigureAwait(false);

                matched[index] = declarations.Any();
            }).ConfigureAwait(false);

        var narrowed = projects.Where((_, index) => matched[index]).ToArray();

        return narrowed.Length is 0 ? projects : narrowed;
    }

    private static async Task<string[]> NearestAsync(
        LoadedWorkspace workspace,
        string symbolId,
        CancellationToken cancellationToken)
    {
        var name = LastSegment(symbolId);
        var found = await SymbolSearch.FindAsync(workspace, name, null, 3, cancellationToken).ConfigureAwait(false);

        return [.. found.Select(symbol => SymbolId.From(symbol).Value)];
    }

    private static string LastSegment(string symbolId)
    {
        var withoutPrefix = symbolId.Length > 2 && symbolId[1] is ':' ? symbolId[2..] : symbolId;
        var withoutParameters = withoutPrefix.Split('(')[0];
        var separator = withoutParameters.LastIndexOf('.');
        var name = separator < 0 ? withoutParameters : withoutParameters[(separator + 1)..];
        var arity = name.IndexOf('`', StringComparison.Ordinal);

        return arity < 0 ? name : name[..arity];
    }
}
