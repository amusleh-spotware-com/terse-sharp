using Microsoft.CodeAnalysis;

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
        var found = new List<ISymbol>();

        foreach (var project in workspace.Solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

            if (compilation is null)
                continue;

            found.AddRange(DocumentationCommentId.GetSymbolsForDeclarationId(symbolId, compilation));
        }

        return found;
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
        var withoutParameters = symbolId.Split('(')[0];
        var separator = withoutParameters.LastIndexOf('.');

        return separator < 0 ? withoutParameters : withoutParameters[(separator + 1)..];
    }
}
