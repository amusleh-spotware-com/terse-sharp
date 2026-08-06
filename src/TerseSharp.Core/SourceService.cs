using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class SourceService
{
    public static async Task<Result<string>> OfSymbolAsync(
        string root,
        ISymbol symbol,
        bool verbose,
        CancellationToken cancellationToken)
    {
        var references = symbol.DeclaringSyntaxReferences;

        if (references.Length is 0)
            return Result.Fail<string>(Errors.SymbolNotFound(SymbolId.From(symbol).Value, []));

        var response = new ResponseBuilder("get_symbol_source", SymbolId.From(symbol).Value).Verbose(verbose);

        foreach (var reference in references)
            await AppendAsync(root, response, reference, verbose, cancellationToken).ConfigureAwait(false);

        return Result.Ok(response.ToString());
    }

    private static async Task AppendAsync(
        string root,
        ResponseBuilder response,
        SyntaxReference reference,
        bool verbose,
        CancellationToken cancellationToken)
    {
        var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
        var span = node.GetLocation().GetLineSpan();
        var source = node.ToFullString();

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"{PositionFormat.Relative(root, span.Path)}:{span.StartLinePosition.Line + 1}-{span.EndLinePosition.Line + 1}"));
        response.Line(verbose ? source.Trim() : TextCompressor.Source(source));
    }

    public static string Describe(string root, ISymbol symbol, bool verbose)
    {
        var id = SymbolId.From(symbol).Value;
        var response = new ResponseBuilder("get_symbol", id).Verbose(verbose);

        if (!verbose)
            response.Note(id);

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"{SymbolFormat.Kind(symbol)} {SymbolFormat.Accessibility(symbol)} {SymbolFormat.Describe(symbol)}"));
        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"at {SymbolFormat.Location(root, symbol)} in {symbol.ContainingNamespace?.ToDisplayString() ?? "-"}"));

        var documentation = symbol.GetDocumentationCommentXml(CultureInfo.InvariantCulture);

        return string.IsNullOrWhiteSpace(documentation)
            ? response.ToString()
            : response.Line(verbose ? documentation.Trim() : TextCompressor.Source(documentation)).ToString();
    }

    public static async Task<string> OfSymbolsAsync(
            LoadedWorkspace workspace,
            IReadOnlyList<string> symbolIds,
            bool verbose,
            CancellationToken cancellationToken)
    {
        var response = new ResponseBuilder("get_symbol_source", string.Join(", ", symbolIds)).Verbose(verbose);

        response.Summary(symbolIds.Count, symbolIds.Count, "symbols");

        foreach (var symbolId in symbolIds)
            await AppendResolvedAsync(workspace, response, symbolId, verbose, cancellationToken).ConfigureAwait(false);

        return response.ToString();
    }

    private static async Task AppendResolvedAsync(
        LoadedWorkspace workspace,
        ResponseBuilder response,
        string symbolId,
        bool verbose,
        CancellationToken cancellationToken)
    {
        var resolved = await SymbolLookup.ResolveAsync(workspace, symbolId, cancellationToken).ConfigureAwait(false);

        if (!resolved.IsOk)
        {
            response.Note("NOT_RESOLVED " + symbolId + "  " + resolved.Error!.Message);

            return;
        }

        var references = resolved.Value!.DeclaringSyntaxReferences;

        if (references.Length is 0)
        {
            response.Note("NO_SOURCE " + symbolId + "  it resolves to metadata, so this workspace holds no source for it");

            return;
        }

        foreach (var reference in references)
            await AppendAsync(workspace.Root, response, reference, verbose, cancellationToken).ConfigureAwait(false);
    }
}
