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

        var updated = await Renamer
            .RenameSymbolAsync(workspace.Solution, symbol, new SymbolRenameOptions(), newName, cancellationToken)
            .ConfigureAwait(false);

        var changed = ChangedDocuments(workspace.Solution, updated);

        return changed.Length is 0
            ? Result.Ok(Unchanged(symbol, newName))
            : await EditGate.ApplyAsync(workspace, updated, changed, options, cancellationToken).ConfigureAwait(false);
    }

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
}

internal static class SyntaxFacts
{
    public static bool IsValidIdentifier(string name) =>
        Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(name);
}
