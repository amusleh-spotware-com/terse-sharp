using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Rename;

namespace TerseSharp.Core;

public static class RenameService
{
    public static async Task<Result<string>> RenameAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        string newName,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        if (!SyntaxFacts.IsValidIdentifier(newName))
            return Result.Fail<string>(Errors.Invalid($"'{newName}' is not a valid C# identifier", "pass a valid identifier"));

        if (await RazorRename.TryAsync(workspace, symbol, newName, options, cancellationToken).ConfigureAwait(false) is { } razor)
            return razor;

        var updated = await Renamer
            .RenameSymbolAsync(workspace.Solution, symbol, new SymbolRenameOptions(), newName, cancellationToken)
            .ConfigureAwait(false);

        var changed = ChangedDocuments(workspace.Solution, updated);

        if (changed.Length is 0)
            return Result.Ok(Unchanged(symbol, newName));

        var applied = await EditGate.ApplyAsync(workspace, updated, changed, options, cancellationToken).ConfigureAwait(false);

        if (!applied.IsOk)
            return applied;

        var withXaml = await WithXaml(workspace, symbol, newName, options, applied.Value!).ConfigureAwait(false);

        return Result.Ok(await WithTextAsync(updated, changed, symbol.Name, workspace.Root, withXaml, cancellationToken).ConfigureAwait(false));
    }
    private static async Task<string> WithXaml(
        LoadedWorkspace workspace,
        ISymbol symbol,
        string newName,
        EditOptions options,
        string applied)
    {
        var xaml = await XamlRename.Apply(workspace, symbol, newName, options.DryRun).ConfigureAwait(false);

        if (xaml.Sites is 0 && xaml.Skipped.Count is 0)
            return applied;

        var response = new ResponseBuilder(string.Empty, string.Empty);

        response.Note(applied);
        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"xaml: {xaml.Sites} site(s) in {xaml.Files} file(s) {(options.DryRun ? "would be" : "were")} rewritten"));

        foreach (var skipped in xaml.Skipped)
            response.Note(Describe(skipped));

        return response.ToString().TrimStart('\n');
    }

    private static string Describe(XamlUsage usage) => string.Create(
        CultureInfo.InvariantCulture,
        $"{usage.File}:{usage.Line}  {usage.Confidence}  {usage.Kind}  {usage.Text}  NOT rewritten, no declared data context proves it binds this member");

    private static string Unchanged(ISymbol symbol, string newName)
    {
        var response = new ResponseBuilder("rename_symbol", SymbolId.From(symbol).Value);

        response.Summary(0, 0, "files changed");
        response.Note(string.Create(CultureInfo.InvariantCulture, $"no occurrence changed to '{newName}'"));

        return response.ToString();
    }

    private static DocumentId[] ChangedDocuments(Solution before, Solution after) =>
        [.. after.GetChanges(before)
            .GetProjectChanges()
            .SelectMany(change => change.GetChangedDocuments())];

    private const int MaxLingering = 10;

    private static async Task<string> WithTextAsync(
        Solution updated,
        DocumentId[] changed,
        string oldName,
        string root,
        string applied,
        CancellationToken cancellationToken)
    {
        var lingering = new List<string>();

        foreach (var id in changed)
        {
            if (updated.GetDocument(id) is { } document)
                await ScanAsync(document, oldName, root, lingering, cancellationToken).ConfigureAwait(false);
        }

        if (lingering.Count is 0)
            return applied;

        var response = new ResponseBuilder(string.Empty, string.Empty);

        response.Note(applied);

        foreach (var note in lingering)
            response.Note(note);

        if (lingering.Count >= MaxLingering)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"only the first {MaxLingering} unrewritten occurrences are listed"));

        return response.ToString().TrimStart('\n');
    }

    private static async Task ScanAsync(
        Document document,
        string oldName,
        string root,
        List<string> lingering,
        CancellationToken cancellationToken)
    {
        if (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is not { } syntax)
            return;

        var source = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var text = source.ToString();
        var path = PositionFormat.Relative(root, document.FilePath ?? document.Name);

        for (var at = text.IndexOf(oldName, StringComparison.Ordinal); at >= 0 && lingering.Count < MaxLingering; at = text.IndexOf(oldName, at + oldName.Length, StringComparison.Ordinal))
        {
            if (Bounded(text, at, oldName.Length) && Carrier(syntax, at) is { } carrier)
                lingering.Add(Lingering(path, source.Lines.GetLinePosition(at).Line + 1, carrier, oldName));
        }
    }

    private static string? Carrier(SyntaxNode syntax, int at)
    {
        var trivia = syntax.FindTrivia(at);

        if (trivia.RawKind is not 0)
            return IsComment(trivia) ? "comment" : null;

        return IsText(syntax.FindToken(at)) ? "string" : null;
    }

    private static bool IsComment(SyntaxTrivia trivia) =>
        trivia.RawKind is (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineCommentTrivia
            or (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.MultiLineCommentTrivia
            or (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineDocumentationCommentTrivia
            or (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.MultiLineDocumentationCommentTrivia;

    private static bool IsText(SyntaxToken token) =>
        token.RawKind is (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralToken
            or (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineRawStringLiteralToken
            or (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.MultiLineRawStringLiteralToken
            or (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.InterpolatedStringTextToken
            or (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.Utf8StringLiteralToken;

    private static bool Bounded(string text, int at, int length) =>
        (at is 0 || !IsWordCharacter(text[at - 1]))
        && (at + length >= text.Length || !IsWordCharacter(text[at + length]));

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value is '_';

    private static string Lingering(string path, int line, string carrier, string oldName) => string.Create(
        CultureInfo.InvariantCulture,
        $"{path}:{line}  HEURISTIC  {carrier}  {oldName}  NOT rewritten, a match inside a {carrier} is not decidable");
}

internal static class SyntaxFacts
{
    public static bool IsValidIdentifier(string name) =>
        Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(name);
}
