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

        return applied.IsOk ? Result.Ok(WithXaml(workspace, symbol, newName, options, applied.Value!)) : applied;
    }
    private static string WithXaml(
        LoadedWorkspace workspace,
        ISymbol symbol,
        string newName,
        EditOptions options,
        string applied)
    {
        var xaml = XamlRename.Apply(workspace, symbol, newName, options.DryRun);

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
}

internal static class SyntaxFacts
{
    public static bool IsValidIdentifier(string name) =>
        Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(name);
}
