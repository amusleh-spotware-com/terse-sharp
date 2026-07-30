using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace TerseSharp.Core;

public static class FormatService
{
    public static Task<Result<string>> FormatAsync(
        LoadedWorkspace workspace,
        string? path,
        EditOptions options,
        CancellationToken cancellationToken) =>
        RewriteAsync(workspace, path, options, FormatOnlyAsync, cancellationToken);

    public static Task<Result<string>> CleanupAsync(
        LoadedWorkspace workspace,
        string? path,
        EditOptions options,
        CancellationToken cancellationToken) =>
        RewriteAsync(workspace, path, options, CleanDocumentAsync, cancellationToken);

    private static async Task<Result<string>> RewriteAsync(
        LoadedWorkspace workspace,
        string? path,
        EditOptions options,
        Func<Document, CancellationToken, Task<Document>> rewrite,
        CancellationToken cancellationToken)
    {
        var documents = Targets(workspace, path);

        if (documents.Length is 0)
            return Result.Fail<string>(Errors.DocumentNotFound(path ?? "solution"));

        var updated = workspace.Solution;

        foreach (var document in documents)
            updated = await ApplyAsync(updated, document.Id, rewrite, cancellationToken).ConfigureAwait(false);

        return await EditGate
            .ApplyAsync(workspace, updated, [.. documents.Select(document => document.Id)], options, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Solution> ApplyAsync(
        Solution solution,
        DocumentId id,
        Func<Document, CancellationToken, Task<Document>> rewrite,
        CancellationToken cancellationToken)
    {
        var document = solution.GetDocument(id);

        if (document is null)
            return solution;

        var rewritten = await rewrite(document, cancellationToken).ConfigureAwait(false);
        var root = await rewritten.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        return root is null ? solution : solution.WithDocumentSyntaxRoot(id, root);
    }

    private static async Task<Document> FormatOnlyAsync(Document document, CancellationToken cancellationToken)
    {
        var options = await document.GetOptionsAsync(cancellationToken).ConfigureAwait(false);

        return await Formatter.FormatAsync(document, options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Document> CleanDocumentAsync(Document document, CancellationToken cancellationToken)
    {
        var withoutUnused = await RemoveUnusedUsingsAsync(document, cancellationToken).ConfigureAwait(false);
        var sorted = await SortUsingsAsync(withoutUnused, cancellationToken).ConfigureAwait(false);

        return await FormatOnlyAsync(sorted, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Document> RemoveUnusedUsingsAsync(Document document, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (model is null || root is null)
            return document;

        var unnecessary = UnnecessaryUsings(model, root, cancellationToken);

        return unnecessary.Length is 0
            ? document
            : document.WithSyntaxRoot(root.RemoveNodes(unnecessary, SyntaxRemoveOptions.KeepNoTrivia)!);
    }

    private static UsingDirectiveSyntax[] UnnecessaryUsings(SemanticModel model, SyntaxNode root, CancellationToken cancellationToken) =>
        [.. model
            .GetDiagnostics(cancellationToken: cancellationToken)
            .Where(diagnostic => diagnostic.Id is "CS8019")
            .Select(diagnostic => root.FindNode(diagnostic.Location.SourceSpan))
            .OfType<UsingDirectiveSyntax>()];

    private static async Task<Document> SortUsingsAsync(Document document, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is not CompilationUnitSyntax unit || unit.Usings.Count < 2)
            return document;

        var sorted = unit.Usings.OrderBy(Rank).ThenBy(Name, StringComparer.Ordinal).ToArray();

        return document.WithSyntaxRoot(unit.WithUsings(SyntaxFactory.List(sorted)));
    }

    private static int Rank(UsingDirectiveSyntax directive) =>
        Name(directive).StartsWith("System", StringComparison.Ordinal) ? 0 : 1;

    private static string Name(UsingDirectiveSyntax directive) => directive.Name?.ToString() ?? string.Empty;

    private static Document[] Targets(LoadedWorkspace workspace, string? path)
    {
        if (path is null)
            return [.. workspace.Solution.Projects.SelectMany(project => project.Documents)];

        var document = DocumentLookup.Find(workspace, path);

        return document is null ? [] : [document];
    }
}
