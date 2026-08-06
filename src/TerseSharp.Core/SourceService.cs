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
}
