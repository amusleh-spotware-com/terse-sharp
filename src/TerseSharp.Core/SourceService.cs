using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class SourceService
{
    public static async Task<Result<string>> OfSymbolAsync(
        string root,
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        var references = symbol.DeclaringSyntaxReferences;

        if (references.Length is 0)
            return Result.Fail<string>(Errors.SymbolNotFound(SymbolId.From(symbol).Value, []));

        var response = new ResponseBuilder("get_symbol_source", SymbolId.From(symbol).Value);

        foreach (var reference in references)
            await AppendAsync(root, response, reference, cancellationToken).ConfigureAwait(false);

        return Result.Ok(response.ToString());
    }

    private static async Task AppendAsync(
        string root,
        ResponseBuilder response,
        SyntaxReference reference,
        CancellationToken cancellationToken)
    {
        var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
        var span = node.GetLocation().GetLineSpan();

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"{PositionFormat.Relative(root, span.Path)}:{span.StartLinePosition.Line + 1}-{span.EndLinePosition.Line + 1}"));
        response.Line(node.ToFullString().Trim());
    }

    public static string Describe(string root, ISymbol symbol)
    {
        var response = new ResponseBuilder("get_symbol", SymbolId.From(symbol).Value);

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"{SymbolFormat.Kind(symbol)} {SymbolFormat.Accessibility(symbol)} {SymbolFormat.Describe(symbol)}"));
        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"at {SymbolFormat.Location(root, symbol)} in {symbol.ContainingNamespace?.ToDisplayString() ?? "-"}"));

        var documentation = symbol.GetDocumentationCommentXml(CultureInfo.InvariantCulture);

        return string.IsNullOrWhiteSpace(documentation) ? response.ToString() : response.Line(documentation.Trim()).ToString();
    }
}
