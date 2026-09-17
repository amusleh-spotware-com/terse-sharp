using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace TerseSharp.Core;

public static class FormatService
{
    public static async Task<Result<string>> RunAsync(
        LoadedWorkspace workspace,
        FixScope scope,
        FixRequest request,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var documents = Scoped(workspace, scope);

        if (documents.Length is 0)
            return Result.Fail<string>(Empty(scope));

        if (request.Mode is FixMode.Ci)
            return await CiAsync(workspace, documents, request, options, cancellationToken).ConfigureAwait(false);

        var outcome = request.AppliesCodeFixes
            ? await CodeFixService.ApplyAsync(workspace.Solution, documents, request, cancellationToken).ConfigureAwait(false)
            : new FixOutcome(workspace.Solution, []);

        var updated = request.Reformats
            ? await RewriteAsync(outcome.Solution, documents, Rewriter(request), cancellationToken).ConfigureAwait(false)
            : outcome.Solution;

        if (request.Verify)
            return Result.Ok(await VerifyAsync(workspace, outcome.Solution, updated, documents, options.Tool, outcome, request, cancellationToken).ConfigureAwait(false));

        var reformatted = request.Reformats
            && await RewroteAsync(outcome.Solution, updated, documents, cancellationToken).ConfigureAwait(false);

        var ungoverned = await UngovernedAsync(workspace, documents, reformatted, cancellationToken).ConfigureAwait(false);
        var applied = await EditGate.ApplyAsync(workspace, updated, documents, options, cancellationToken).ConfigureAwait(false);

        return Annotated(applied, outcome.Unfixed, ungoverned);
    }

    private static Func<Document, CancellationToken, Task<Document>> Rewriter(FixRequest request) =>
        request.CleansUsings ? CleanDocumentAsync : FormatOnlyAsync;

    private static Result<string> Annotated(Result<string> applied, IReadOnlyList<string> unfixed, string? note = null)
    {
        if (!applied.IsOk || (unfixed.Count is 0 && note is not { Length: > 0 }))
            return applied;

        var text = new StringBuilder(applied.Value);

        foreach (var line in unfixed)
            text.Append('\n').Append(line);

        if (note is { Length: > 0 })
            text.Append('\n').Append(note);

        return Result.Ok(text.ToString());
    }

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
        Solution fixedUp,
        Solution updated,
        IReadOnlyList<DocumentId> documents,
        string tool,
        FixOutcome outcome,
        FixRequest request,
        CancellationToken cancellationToken)
    {
        var changed = await ChangedAsync(workspace, fixedUp, updated, documents, cancellationToken).ConfigureAwait(false);

        if (changed.Length is 0 && outcome.Unfixed.Count is 0)
            return "clean";

        var note = RunsTheFormatterCiDoesNot(request, changed)
            ? "this mode also runs the whitespace formatter, which the CI format step does not - the byte-equivalent CI pair is cleanup verify=true fix=ci"
            : null;

        return Verdict(tool, changed, outcome.Unfixed, note);
    }
    private static async Task<VerifiedFile[]> ChangedAsync(
        LoadedWorkspace workspace,
        Solution fixedUp,
        Solution updated,
        IReadOnlyList<DocumentId> documents,
        CancellationToken cancellationToken)
    {
        var changed = new List<VerifiedFile>();

        foreach (var id in documents)
        {
            if (!await DiffersAsync(workspace.Solution, updated, id, cancellationToken).ConfigureAwait(false))
                continue;

            var byFixers = await DiffersAsync(workspace.Solution, fixedUp, id, cancellationToken).ConfigureAwait(false);
            var byFormatter = await DiffersAsync(fixedUp, updated, id, cancellationToken).ConfigureAwait(false);

            changed.Add(new VerifiedFile(
                PositionFormat.Relative(workspace.Root, updated.GetDocument(id)?.FilePath),
                ChangedBy(byFixers, byFormatter)));
        }

        changed.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));

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
        var formatted = await Formatter.FormatAsync(document, options, cancellationToken).ConfigureAwait(false);

        return await CollapsedAsync(formatted, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Document> CleanDocumentAsync(Document document, CancellationToken cancellationToken)
    {
        var withoutUnused = await RemoveUnusedUsingsAsync(document, cancellationToken).ConfigureAwait(false);

        return await FormatOnlyAsync(withoutUnused, cancellationToken).ConfigureAwait(false);
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

    private static DocumentId[] Scoped(LoadedWorkspace workspace, FixScope scope) =>
        DocumentScope.Select(workspace, scope.Path, scope.ChangedOnly);

    private static TerseError Empty(FixScope scope) => scope.ChangedOnly
        ? Errors.Invalid(
            "no document under that scope was modified since this workspace started tracking changes",
            "drop changed=true to sweep the whole scope, or pass path= to name the files yourself")
        : Errors.DocumentNotFound(scope.Path ?? "solution");

    private sealed record VerifiedFile(string Path, string ChangedBy);

    private static string ChangedBy(bool byFixers, bool byFormatter) => (byFixers, byFormatter) switch
    {
        (true, true) => "fixers+whitespace",
        (true, false) => "fixers",
        _ => "whitespace",
    };

    private static bool RunsTheFormatterCiDoesNot(FixRequest request, VerifiedFile[] changed) =>
        request.Reformats
        && Array.Exists(changed, file => file.ChangedBy is "whitespace" or "fixers+whitespace");

    private static async Task<Document> CollapsedAsync(Document document, CancellationToken cancellationToken)
    {
        if (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is not { } root)
            return document;

        var collapsed = new BlankLineCollapser().Visit(root);

        return collapsed is null || collapsed == root ? document : document.WithSyntaxRoot(collapsed);
    }

    private sealed class BlankLineCollapser : CSharpSyntaxRewriter
    {
        public override SyntaxToken VisitToken(SyntaxToken token) =>
            token.HasLeadingTrivia ? token.WithLeadingTrivia(Collapsed(token.LeadingTrivia)) : token;

        private static SyntaxTriviaList Collapsed(SyntaxTriviaList trivia)
        {
            var kept = new List<SyntaxTrivia>(trivia.Count);
            var seen = 0;
            var allowed = 1;

            foreach (var item in trivia)
            {
                if (!Drops(item, ref seen, ref allowed))
                    kept.Add(item);
            }

            return kept.Count == trivia.Count ? trivia : SyntaxFactory.TriviaList(kept);
        }

        private static bool Drops(SyntaxTrivia item, ref int seen, ref int allowed)
        {
            if (item.IsKind(SyntaxKind.WhitespaceTrivia))
                return false;

            if (!item.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                seen = 0;
                allowed = 2;

                return false;
            }

            return ++seen > allowed;
        }
    }

    private static async Task<Result<string>> CiAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<DocumentId> documents,
        FixRequest request,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var styled = await CodeFixService.ApplyAsync(workspace.Solution, documents, request with { Mode = FixMode.Style }, cancellationToken).ConfigureAwait(false);
        var fixedUp = await CodeFixService.ApplyAsync(styled.Solution, documents, request with { Mode = FixMode.Analyzers }, cancellationToken).ConfigureAwait(false);
        string[] unfixed = [.. styled.Unfixed, .. fixedUp.Unfixed];

        if (request.Verify)
            return Result.Ok(await CiVerifiedAsync(workspace, styled.Solution, fixedUp.Solution, documents, options.Tool, unfixed, cancellationToken).ConfigureAwait(false));

        var applied = await EditGate.ApplyAsync(workspace, fixedUp.Solution, documents, options, cancellationToken).ConfigureAwait(false);

        return Annotated(applied, unfixed);
    }

    private static async Task<string> CiVerifiedAsync(
        LoadedWorkspace workspace,
        Solution styled,
        Solution fixedUp,
        IReadOnlyList<DocumentId> documents,
        string tool,
        string[] unfixed,
        CancellationToken cancellationToken)
    {
        var changed = await CiChangedAsync(workspace, styled, fixedUp, documents, cancellationToken).ConfigureAwait(false);

        return changed.Length is 0 && unfixed.Length is 0 ? "clean" : Verdict(tool, changed, unfixed, null);
    }

    private static async Task<VerifiedFile[]> CiChangedAsync(
        LoadedWorkspace workspace,
        Solution styled,
        Solution fixedUp,
        IReadOnlyList<DocumentId> documents,
        CancellationToken cancellationToken)
    {
        var changed = new List<VerifiedFile>(documents.Count);

        foreach (var id in documents)
        {
            if (await CiFileAsync(workspace, styled, fixedUp, id, cancellationToken).ConfigureAwait(false) is { } file)
                changed.Add(file);
        }

        changed.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));

        return [.. changed];
    }

    private static async Task<VerifiedFile?> CiFileAsync(
        LoadedWorkspace workspace,
        Solution styled,
        Solution fixedUp,
        DocumentId id,
        CancellationToken cancellationToken)
    {
        if (!await DiffersAsync(workspace.Solution, fixedUp, id, cancellationToken).ConfigureAwait(false))
            return null;

        var byStyle = await DiffersAsync(workspace.Solution, styled, id, cancellationToken).ConfigureAwait(false);
        var byAnalyzers = await DiffersAsync(styled, fixedUp, id, cancellationToken).ConfigureAwait(false);

        return new VerifiedFile(
            PositionFormat.Relative(workspace.Root, fixedUp.GetDocument(id)?.FilePath),
            CiChangedBy(byStyle, byAnalyzers));
    }

    private static string CiChangedBy(bool byStyle, bool byAnalyzers) => (byStyle, byAnalyzers) switch
    {
        (true, true) => "style+analyzers",
        (true, false) => "style",
        _ => "analyzers",
    };

    private static string Verdict(string tool, VerifiedFile[] changed, IReadOnlyList<string> unfixed, string? note)
    {
        var response = new ResponseBuilder(tool, "verify");

        response.Summary(changed.Length, changed.Length, "files would change");

        if (changed.Length > 0)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"VERIFY_FAILED {changed.Length} file(s) would change"));

        if (note is not null)
            response.Note(note);

        foreach (var file in changed)
            response.Line(file.Path + "  " + file.ChangedBy);

        foreach (var line in unfixed)
            response.Note(line);

        return response.ToString();
    }

    private static async Task<bool> RewroteAsync(
        Solution before,
        Solution after,
        DocumentId[] documents,
        CancellationToken cancellationToken)
    {
        foreach (var id in documents)
        {
            if (await DiffersAsync(before, after, id, cancellationToken).ConfigureAwait(false))
                return true;
        }

        return false;
    }

    private static async Task<string?> UngovernedAsync(
        LoadedWorkspace workspace,
        DocumentId[] documents,
        bool reformatted,
        CancellationToken cancellationToken)
    {
        if (!reformatted)
            return null;

        var probed = false;

        foreach (var id in documents.DistinctBy(document => document.ProjectId))
        {
            if (workspace.Solution.GetDocument(id) is not { } document)
                continue;

            probed = true;

            if (await GovernedAsync(document, cancellationToken).ConfigureAwait(false))
                return null;
        }

        return probed ? Ungoverned : null;
    }

    private static async Task<bool> GovernedAsync(Document document, CancellationToken cancellationToken)
    {
        var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);

        return tree is not null
            && document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(tree).TryGetValue("indent_style", out _);
    }

    private const string Ungoverned = "NOTE no .editorconfig at or above these files sets indent_style, so whitespace followed Roslyn's own defaults - which may differ from this repository's convention; a ReSharper .sln.DotSettings is not read";
}
