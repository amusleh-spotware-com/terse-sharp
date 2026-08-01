using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerseSharp.Core;

public static class SymbolLookup
{
    private const int NameCap = 100;

    public static async Task<Result<ISymbol>> ResolveAsync(
        LoadedWorkspace workspace,
        string symbolId,
        CancellationToken cancellationToken)
    {
        if (!SymbolReference.IsDocumentationId(symbolId))
            return await ByNameAsync(workspace, symbolId, cancellationToken).ConfigureAwait(false);

        var matches = await FindAllAsync(workspace, symbolId, cancellationToken).ConfigureAwait(false);
        var distinct = matches.DistinctBy(Describe, StringComparer.Ordinal).ToArray();

        if (distinct.Length is 1)
            return Result.Ok(distinct[0]);

        if (distinct.Length > 1)
            return Result.Fail<ISymbol>(Errors.AmbiguousSymbol(symbolId, [.. distinct.Select(Describe)]));

        var nearest = await NearestAsync(workspace, symbolId, cancellationToken).ConfigureAwait(false);

        return Result.Fail<ISymbol>(Errors.SymbolNotFound(symbolId, nearest));
    }

    private static async Task<Result<ISymbol>> ByNameAsync(
        LoadedWorkspace workspace,
        string text,
        CancellationToken cancellationToken)
    {
        if (SymbolReference.Parse(text) is not { } query)
        {
            return Result.Fail<ISymbol>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{text}' is neither a symbol id nor a name"),
                "pass a documentation id such as M:Ns.Type.Member(Ns.Arg), or a name such as Type.Member"));
        }

        var found = (await SymbolSearch.FindAsync(workspace, query.Member, null, NameCap + 1, cancellationToken).ConfigureAwait(false)).Ranked;

        if (found.Count > NameCap)
            return Result.Fail<ISymbol>(Errors.SaturatedName(text, NameCap));

        var named = found.Where(symbol => string.Equals(symbol.Name, query.Member, StringComparison.Ordinal)).ToArray();
        var matches = named.Where(symbol => SymbolReference.Matches(symbol, query)).DistinctBy(Describe, StringComparer.Ordinal).ToArray();

        return Chosen(text, matches, found);
    }

    private static Result<ISymbol> Chosen(string text, ISymbol[] matches, IReadOnlyList<ISymbol> found) => matches switch
    {
        [var only] => Result.Ok(only),
        [] => Result.Fail<ISymbol>(Errors.SymbolNotFound(text, [.. found.Take(3).Select(Addressable)])),
        _ => Result.Fail<ISymbol>(Errors.AmbiguousName(
            text,
            [.. matches.Take(10).Select(symbol => SymbolId.From(symbol).Value)],
            matches.Length)),
    };

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

        return [.. found.Ranked.Select(symbol => SymbolId.From(symbol).Value)];
    }

    private static string LastSegment(string symbolId)
    {
        var text = symbolId.AsSpan();
        var withoutPrefix = text.Length > 2 && text[1] is ':' ? text[2..] : text;
        var withoutParameters = withoutPrefix.IndexOf('(') is var open and >= 0 ? withoutPrefix[..open] : withoutPrefix;
        var separator = withoutParameters.LastIndexOf('.');
        var name = separator < 0 ? withoutParameters : withoutParameters[(separator + 1)..];
        var arity = name.IndexOf('`');

        return new string(arity < 0 ? name : name[..arity]);
    }
    private static string Addressable(ISymbol symbol) =>
        SymbolReference.RoundTrips(symbol) ? SymbolReference.Brief(symbol) : SymbolId.From(symbol).Value;
}
