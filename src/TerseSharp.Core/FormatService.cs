using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace TerseSharp.Core;

public static class FormatService
{
    public static async Task<Result<string>> RunAsync(
        LoadedWorkspace workspace,
        string? path,
        FixRequest request,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var documents = Targets(workspace, path);

        if (documents.Length is 0)
            return Result.Fail<string>(Errors.DocumentNotFound(path ?? "solution"));

        var outcome = request.AppliesCodeFixes
            ? await CodeFixService.ApplyAsync(workspace.Solution, documents, request, cancellationToken).ConfigureAwait(false)
            : new FixOutcome(workspace.Solution, []);

        var updated = await RewriteAsync(outcome.Solution, documents, Rewriter(request), cancellationToken).ConfigureAwait(false);

        if (request.Verify)
            return Result.Ok(await VerifyAsync(workspace, updated, documents, options.Tool, outcome, cancellationToken).ConfigureAwait(false));

        var applied = await EditGate.ApplyAsync(workspace, updated, documents, options, cancellationToken).ConfigureAwait(false);

        return Annotated(applied, outcome.Unfixed);
    }

    private static Func<Document, CancellationToken, Task<Document>> Rewriter(FixRequest request) =>
        request.CleansUsings ? CleanDocumentAsync : FormatOnlyAsync;

    private static Result<string> Annotated(Result<string> applied, IReadOnlyList<string> unfixed) =>
        applied.IsOk && unfixed.Count > 0
            ? Result.Ok(applied.Value + "\n" + string.Join("\n", unfixed))
            : applied;

    private static async Task<Solution> RewriteAsync(
        Solution solution,
        IReadOnlyList<DocumentId> documents,
        Func<Document, CancellationToken, Task<Document>> rewrite,
        CancellationToken cancellationToken)
    {
        var updated = solution;

        foreach (var id in documents)
            updated = await ApplyAsync(updated, id, rewrite, cancellationToken).ConfigureAwait(false);

        return updated;
    }

    private static async Task<string> VerifyAsync(
        LoadedWorkspace workspace,
        Solution updated,
        IReadOnlyList<DocumentId> documents,
        string tool,
        FixOutcome outcome,
        CancellationToken cancellationToken)
    {
        var changed = await ChangedAsync(workspace, updated, documents, cancellationToken).ConfigureAwait(false);
        var response = new ResponseBuilder(tool, "verify");

        response.Summary(changed.Length, changed.Length, "files would change");
        response.Note(changed.Length is 0
            ? "clean"
            : string.Create(CultureInfo.InvariantCulture, $"VERIFY_FAILED {changed.Length} file(s) would change"));

        foreach (var file in changed)
            response.Line(file);

        foreach (var line in outcome.Unfixed)
            response.Note(line);

        return response.ToString();
    }

    private static async Task<string[]> ChangedAsync(
        LoadedWorkspace workspace,
        Solution updated,
        IReadOnlyList<DocumentId> documents,
        CancellationToken cancellationToken)
    {
        var changed = new List<string>();

        foreach (var id in documents)
        {
            if (await DiffersAsync(workspace.Solution, updated, id, cancellationToken).ConfigureAwait(false))
                changed.Add(PositionFormat.Relative(workspace.Root, updated.GetDocument(id)?.FilePath));
        }

        changed.Sort(StringComparer.Ordinal);

        return [.. changed];
    }

    private static async Task<bool> DiffersAsync(
        Solution before,
        Solution after,
        DocumentId id,
        CancellationToken cancellationToken)
    {
        var original = before.GetDocument(id);
        var updated = after.GetDocument(id);

        if (original is null || updated is null)
            return false;

        var originalText = await original.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var updatedText = await updated.GetTextAsync(cancellationToken).ConfigureAwait(false);

        return !originalText.ContentEquals(updatedText);
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

    private static DocumentId[] Targets(LoadedWorkspace workspace, string? path)
    {
        if (path is null)
            return [.. Editable(workspace).Select(document => document.Id)];

        if (string.IsNullOrWhiteSpace(path))
            return [];

        if (IsGlob(path))
            return [.. Matching(workspace, FileGlob.Compile(path)).Select(document => document.Id)];

        if (DocumentLookup.Find(workspace, path) is { } document)
            return [document.Id];

        return [.. UnderDirectory(workspace, path)];
    }

    private static bool IsGlob(string path) =>
        path.Contains('*', StringComparison.Ordinal) || path.Contains('?', StringComparison.Ordinal);

    private static IEnumerable<Document> Sources(LoadedWorkspace workspace) =>
        workspace.Solution.Projects.SelectMany(project => project.Documents);

    private static bool Matches(string root, Document document, FileGlob glob) =>
        document.FilePath is { Length: > 0 } file && glob.MatchesFile(root, file);

    private static IEnumerable<DocumentId> UnderDirectory(LoadedWorkspace workspace, string path)
    {
        var resolved = PathGuard.Resolve(workspace, path);

        return resolved.IsOk && Directory.Exists(resolved.Value)
            ? Editable(workspace).Where(document => Inside(resolved.Value!, document)).Select(document => document.Id)
            : [];
    }

    private static bool Inside(string directory, Document document) =>
        document.FilePath is { Length: > 0 } file && PathBoundary.Contains(directory, file);
    private static IEnumerable<Document> Editable(LoadedWorkspace workspace) =>
        Sources(workspace).Where(document => !GeneratedCode.IsGenerated(workspace.Root, document.FilePath));

    private static IEnumerable<Document> Matching(LoadedWorkspace workspace, FileGlob glob) =>
        Editable(workspace).Where(document => Matches(workspace.Root, document, glob));
}
