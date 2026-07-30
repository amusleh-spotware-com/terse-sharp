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

        if (matches.Count > 0)
            return Result.Ok(matches[0]);

        var nearest = await NearestAsync(workspace, symbolId, cancellationToken).ConfigureAwait(false);

        return Result.Fail<ISymbol>(Errors.SymbolNotFound(symbolId, nearest));
    }

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
