using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
        {
            return MetadataSearch.IsMetadata(symbol)
                ? Result.Ok(Metadata(symbol, format))
                : Result.Fail<string>(Errors.SymbolNotFound(SymbolId.From(symbol).Value, []));
        }

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
        var node = Declaration(await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false));
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
            $"at {Where(root, symbol)} in {symbol.ContainingNamespace?.ToDisplayString() ?? "-"}"));

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
        var resolved = await SymbolLookup.ResolveAsync(workspace, symbolId, path, cancellationToken, referenced: true).ConfigureAwait(false);

        if (!resolved.IsOk)
        {
            response.Note("NOT_RESOLVED " + symbolId + "  " + Unresolved(symbolId, resolved.Error!));
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
            response.Note(Metadata(resolved.Value!, format));
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
        if (format.Verbose
            || symbol is not INamedTypeSymbol { TypeKind: not TypeKind.Delegate } type
            || await CompactAsync(type, format, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var outline = await OutlineService.TypeAsync(workspace, symbol, true, "short", cancellationToken).ConfigureAwait(false);

        return outline.IsOk
            ? outline.Value!.TrimEnd('\n') + string.Create(
                CultureInfo.InvariantCulture,
                $"\nsteer: get_symbol_source symbolId={symbol.Name}.Member for one member's source, verbose=true for the whole type")
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

    private static string Where(string root, ISymbol symbol)
    {
        var location = SymbolFormat.Location(root, symbol);

        return location is "-" && MetadataSearch.IsMetadata(symbol) ? MetadataSearch.Origin(symbol) : location;
    }

    private static string Metadata(ISymbol symbol, SourceFormat format)
    {
        var id = SymbolId.From(symbol).Value;
        var response = new ResponseBuilder("get_symbol_source", id).Verbose(format.Verbose);
        var outline = SymbolId.From(symbol as INamedTypeSymbol ?? symbol.ContainingType ?? symbol).Value;

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"{id}  {MetadataSearch.Origin(symbol)}  metadata - no source to show"));
        response.Line(string.Create(
            CultureInfo.InvariantCulture,
            $"{SymbolFormat.Kind(symbol)} {SymbolFormat.Accessibility(symbol)} {SymbolFormat.Describe(symbol)}"));
        response.Note(string.Create(CultureInfo.InvariantCulture, $"members: get_type_outline symbolId={outline}"));

        return response.ToString();
    }

    private const int MaxInlinedTypeLines = 4;

    private static async Task<bool> CompactAsync(INamedTypeSymbol type, SourceFormat format, CancellationToken cancellationToken)
    {
        if (type.DeclaringSyntaxReferences is not [var only])
            return false;

        var declared = only.SyntaxTree.GetLineSpan(only.Span, cancellationToken);

        if (declared.EndLinePosition.Line - declared.StartLinePosition.Line + 1 > MaxInlinedTypeLines)
            return false;

        var node = await only.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);

        return Inlinable(TextCompressor.Source(format.Comments ? node.ToFullString() : CommentStripper.Without(node)));
    }

    private static bool Inlinable(ReadOnlySpan<char> text)
    {
        var trimmed = text.Trim();

        return trimmed.Count('\n') + 1 <= MaxInlinedTypeLines && trimmed.Length <= MaxInlinedTypeChars;
    }

    private const int MaxInlinedTypeChars = 200;

    private static string Unresolved(string symbolId, TerseError error)
    {
        var message = Trimmed(error.Message, symbolId);

        return string.Equals(error.Remedy, Errors.SearchForTheId, StringComparison.Ordinal)
            ? message
            : message + "  " + error.Remedy;
    }

    private static string Trimmed(string message, string symbolId)
    {
        const string Opening = "symbol '";

        var text = message.AsSpan();
        var length = Opening.Length + symbolId.Length + 2;

        return text.Length > length
            && text.StartsWith(Opening, StringComparison.Ordinal)
            && text.Slice(Opening.Length, symbolId.Length).SequenceEqual(symbolId)
            && text[Opening.Length + symbolId.Length] is '\''
                ? message[length..]
                : message;
    }

    private static SyntaxNode Declaration(SyntaxNode node) =>
            node is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Variables.Count: 1, Parent: BaseFieldDeclarationSyntax field } }
                ? field
                : node;
}
