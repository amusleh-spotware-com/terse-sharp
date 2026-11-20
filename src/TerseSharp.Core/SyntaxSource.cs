namespace TerseSharp.Core;

public readonly record struct SyntaxRead(string SymbolId, string Path, SourceFormat Format);

public static class SyntaxSource
{
    public static async Task<string?> SourceAsync(LoadedWorkspace workspace, SyntaxRead read, CancellationToken cancellationToken)
    {
        if (read.Format.Verbose || await SyntaxDeclaration.UniqueAsync(workspace, read.SymbolId, read.Path, cancellationToken).ConfigureAwait(false) is not { } declaration)
            return null;

        var span = declaration.GetLocation().GetLineSpan();
        var response = new ResponseBuilder("get_symbol_source", read.SymbolId);

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"{PositionFormat.Relative(workspace.Root, span.Path)}:{span.StartLinePosition.Line + 1}-{span.EndLinePosition.Line + 1}"));
        response.Line(TextCompressor.Source(read.Format.Comments ? declaration.ToFullString() : CommentStripper.Without(declaration)));

        return response.ToString();
    }
}
