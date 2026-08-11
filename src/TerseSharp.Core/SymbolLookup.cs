using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerseSharp.Core;

public static class SymbolLookup
{
    private const int NameCap = 100;

    public static Task<Result<ISymbol>> ResolveAsync(
    LoadedWorkspace workspace,
    string symbolId,
    CancellationToken cancellationToken) =>
    ResolveAsync(workspace, symbolId, null, cancellationToken);

    public static async Task<Result<ISymbol>> ResolveAsync(
    LoadedWorkspace workspace,
    string symbolId,
    string? path,
    CancellationToken cancellationToken,
    bool typesOnly = false)
    {
        var requested = SymbolReference.Unescaped(symbolId);

        if (SymbolReference.IsDocumentationId(requested))
            return await ByIdAsync(workspace, requested, cancellationToken).ConfigureAwait(false);

        if (path is not { Length: > 0 } scope)
            return await ByNameAsync(workspace, requested, typesOnly, cancellationToken).ConfigureAwait(false);

        return Typed(await InFileAsync(workspace, requested, scope, cancellationToken).ConfigureAwait(false), typesOnly)
            ?? await ByNameAsync(workspace, requested, typesOnly, cancellationToken).ConfigureAwait(false);
    }

    private static Result<ISymbol>? Typed(Result<ISymbol>? found, bool typesOnly) =>
        typesOnly && found is { IsOk: true, Value: not INamedTypeSymbol } ? null : found;

    private static async Task<Result<ISymbol>> ByNameAsync(
LoadedWorkspace workspace,
string text,
bool typesOnly,
CancellationToken cancellationToken)
    {
        if (SymbolReference.Parse(text) is not { } query)
            return Unparsed(text);

        var found = (await SymbolSearch.FindAsync(workspace, query.Member, null, null, NameCap + 1, cancellationToken).ConfigureAwait(false)).Ranked;

        if (found.Count > NameCap)
        {
            return await ByContainerAsync(workspace, text, query, cancellationToken).ConfigureAwait(false)
                ?? Result.Fail<ISymbol>(Errors.SaturatedName(text, NameCap));
        }

        var named = found.Where(symbol => string.Equals(symbol.Name, query.Member, StringComparison.Ordinal)).ToArray();
        var matches = named.Where(symbol => SymbolReference.Matches(symbol, query)).DistinctBy(Describe, StringComparer.Ordinal).ToArray();

        if (!typesOnly)
            return Chosen(text, matches, found);

        var types = matches.Where(symbol => symbol is INamedTypeSymbol).ToArray();

        return OnlyTypes(text, types, matches.Length - types.Length) ?? Chosen(text, types, found);
    }

    private static Result<ISymbol>? OnlyTypes(string text, ISymbol[] types, int dropped) => types.Length is 0 && dropped > 0
        ? Result.Fail<ISymbol>(Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"'{text}' names no type, and this parameter takes a containing type; {dropped} non-type symbol(s) also match this name"),
            "name the type that declares them, or take its documentation id from search_symbols kind=class"))
        : null;

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
        var found = await SymbolSearch.FindAsync(workspace, name, null, null, 3, cancellationToken).ConfigureAwait(false);

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

    private static async Task<Result<ISymbol>> ByIdAsync(
        LoadedWorkspace workspace,
        string requested,
        CancellationToken cancellationToken)
    {
        var matches = await FindAllAsync(workspace, requested, cancellationToken).ConfigureAwait(false);
        var distinct = matches.DistinctBy(Describe, StringComparer.Ordinal).ToArray();

        if (distinct.Length is 1)
            return Result.Ok(distinct[0]);

        if (distinct.Length > 1)
            return Result.Fail<ISymbol>(Errors.AmbiguousSymbol(requested, [.. distinct.Select(Describe)]));

        var nearest = await NearestAsync(workspace, requested, cancellationToken).ConfigureAwait(false);

        return Result.Fail<ISymbol>(Errors.SymbolNotFound(requested, nearest));
    }

    private static async Task<Result<ISymbol>?> InFileAsync(
        LoadedWorkspace workspace,
        string text,
        string path,
        CancellationToken cancellationToken)
    {
        if (SymbolReference.Parse(text) is not { } query)
            return null;

        if (DocumentLookup.Find(workspace, path) is not { } document)
            return Result.Fail<ISymbol>(Errors.DocumentNotFound(path));

        var declared = await SymbolFinder
            .FindSourceDeclarationsAsync(document.Project, query.Member, ignoreCase: false, cancellationToken)
            .ConfigureAwait(false);

        return Scoped(text, [.. declared
        .Where(symbol => DeclaredIn(symbol, document.FilePath) && SymbolReference.Matches(symbol, query))
        .DistinctBy(Describe, StringComparer.Ordinal)]);
    }

    private static Result<ISymbol>? Scoped(string text, ISymbol[] matches) => matches switch
    {
        [var only] => Result.Ok(only),
        [] => null,
        _ => Result.Fail<ISymbol>(Errors.AmbiguousName(
            text,
            [.. matches.Take(10).Select(symbol => SymbolId.From(symbol).Value)],
            matches.Length)),
    };


    private static bool DeclaredIn(ISymbol symbol, string? filePath) =>
        filePath is { Length: > 0 } && symbol.DeclaringSyntaxReferences.Any(reference =>
            string.Equals(reference.SyntaxTree.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

    private static Result<ISymbol> Unparsed(string text) => Result.Fail<ISymbol>(Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"'{text}' is neither a symbol id nor a name"),
        "pass a documentation id such as M:Ns.Type.Member(Ns.Arg), or a name such as Type.Member"));

    private static async Task<Result<ISymbol>?> ByContainerAsync(
        LoadedWorkspace workspace,
        string text,
        SymbolQuery query,
        CancellationToken cancellationToken)
    {
        if (query.ContainingType is not { Length: > 0 } qualifier)
            return null;

        var name = qualifier[(qualifier.LastIndexOf('.') + 1)..];
        var types = (await SymbolSearch.FindAsync(workspace, name, null, null, NameCap + 1, cancellationToken).ConfigureAwait(false)).Ranked;

        return types.Count > NameCap ? null : Scoped(text, Declared(types, name, query));
    }

    private static ISymbol[] Declared(IReadOnlyList<ISymbol> types, string name, SymbolQuery query) =>
    [
        .. types
        .OfType<INamedTypeSymbol>()
        .Where(type => string.Equals(type.Name, name, StringComparison.Ordinal))
        .SelectMany(type => type.GetMembers(query.Member))
        .Where(symbol => SymbolReference.Matches(symbol, query))
        .DistinctBy(Describe, StringComparer.Ordinal),
];
}
