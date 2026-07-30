using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class SourceService
{
    public static async Task<Result<string>> OfSymbolAsync(
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();

        if (reference is null)
            return Result.Fail<string>(Errors.SymbolNotFound(SymbolId.From(symbol).Value, []));

        var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
        var span = node.GetLocation().GetLineSpan();
        var response = new ResponseBuilder("get_symbol_source", SymbolId.From(symbol).Value);

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"{span.Path}:{span.StartLinePosition.Line + 1}-{span.EndLinePosition.Line + 1}"));

        return Result.Ok(response.Line(node.ToFullString().Trim()).ToString());
    }

    public static string Describe(ISymbol symbol)
    {
        var response = new ResponseBuilder("get_symbol", SymbolId.From(symbol).Value);

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"{SymbolFormat.Kind(symbol)} {SymbolFormat.Accessibility(symbol)} {SymbolFormat.Describe(symbol)}"));
        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"at {SymbolFormat.Location(symbol)} in {symbol.ContainingNamespace?.ToDisplayString() ?? "-"}"));

        var documentation = symbol.GetDocumentationCommentXml(CultureInfo.InvariantCulture);

        return string.IsNullOrWhiteSpace(documentation) ? response.ToString() : response.Line(documentation.Trim()).ToString();
    }
}
