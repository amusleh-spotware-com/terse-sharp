using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class SourceService
{
    public static async Task<Result<string>> OfSymbolAsync(
        string root,
        ISymbol symbol,
        SourceFormat format,
        CancellationToken cancellationToken)
    {
        var references = symbol.DeclaringSyntaxReferences;

        if (references.Length is 0)
            return Result.Fail<string>(Errors.SymbolNotFound(SymbolId.From(symbol).Value, []));

        var response = new ResponseBuilder("get_symbol_source", SymbolId.From(symbol).Value).Verbose(format.Verbose);

        foreach (var reference in references)
            await AppendAsync(root, response, reference, format, cancellationToken).ConfigureAwait(false);

        return Result.Ok(response.ToString());
    }

    private static async Task AppendAsync(
        string root,
        ResponseBuilder response,
        SyntaxReference reference,
        SourceFormat format,
        CancellationToken cancellationToken)
    {
        var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
        var span = node.GetLocation().GetLineSpan();
        var source = format.Comments ? node.ToFullString() : CommentStripper.Without(node);

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"{PositionFormat.Relative(root, span.Path)}:{span.StartLinePosition.Line + 1}-{span.EndLinePosition.Line + 1}"));
        response.Line(format.Verbose ? source.Trim() : TextCompressor.Source(source));
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
    SourceFormat format,
    CancellationToken cancellationToken,
    string? path = null)
    {
        var response = new ResponseBuilder("get_symbol_source", string.Join(", ", symbolIds)).Verbose(format.Verbose);
        response.Summary(symbolIds.Count, symbolIds.Count, "symbols");

        foreach (var symbolId in symbolIds)
            await AppendResolvedAsync(workspace, response, symbolId, format, path, cancellationToken).ConfigureAwait(false);

        return response.ToString();
    }

    private static async Task AppendResolvedAsync(
    LoadedWorkspace workspace,
    ResponseBuilder response,
    string symbolId,
    SourceFormat format,
    string? path,
    CancellationToken cancellationToken)
    {
        var resolved = await SymbolLookup.ResolveAsync(workspace, symbolId, path, cancellationToken).ConfigureAwait(false);

        if (!resolved.IsOk)
        {
            response.Note("NOT_RESOLVED " + symbolId + "  " + resolved.Error!.Message);
            return;
        }

        if (await OutlinedAsync(workspace, resolved.Value!, format, cancellationToken).ConfigureAwait(false) is { } outlined)
        {
            response.Note(outlined);
            return;
        }

        var references = resolved.Value!.DeclaringSyntaxReferences;

        if (references.Length is 0)
        {
            response.Note("NO_SOURCE " + symbolId + "  it resolves to metadata, so this workspace holds no source for it");
            return;
        }

        foreach (var reference in references)
            await AppendAsync(workspace.Root, response, reference, format, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> OutlinedAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        SourceFormat format,
        CancellationToken cancellationToken)
    {
        if (format.Verbose || symbol is not INamedTypeSymbol { TypeKind: not TypeKind.Delegate })
            return null;

        var outline = await OutlineService.TypeAsync(workspace, symbol, true, "short", cancellationToken).ConfigureAwait(false);

        return outline.IsOk
            ? outline.Value! + string.Create(
                CultureInfo.InvariantCulture,
                $"steer: get_symbol_source symbolId={symbol.Name}.Member for one member's source, verbose=true for the whole type")
            : null;
    }

    public static async Task<Result<string>> OfSymbolAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        SourceFormat format,
        CancellationToken cancellationToken)
    {
        var outlined = await OutlinedAsync(workspace, symbol, format, cancellationToken).ConfigureAwait(false);

        return outlined is null
            ? await OfSymbolAsync(workspace.Root, symbol, format, cancellationToken).ConfigureAwait(false)
            : Result.Ok(outlined);
    }
}
