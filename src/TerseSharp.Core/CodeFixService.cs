using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace TerseSharp.Core;

public static class CodeFixService
{
    private const int MaxPasses = 25;

    public static async Task<FixOutcome> ApplyAsync(
        Solution solution,
        IReadOnlyList<DocumentId> documents,
        FixRequest request,
        CancellationToken cancellationToken)
    {
        var unfixed = new List<string>();
        var current = solution;

        foreach (var projectId in documents.Select(document => document.ProjectId).Distinct())
        {
            current = await FixProjectAsync(current, projectId, Scoped(current, documents, projectId), request, unfixed, cancellationToken)
                .ConfigureAwait(false);
        }

        return new FixOutcome(current, unfixed);
    }

    private static HashSet<string> Scoped(Solution solution, IReadOnlyList<DocumentId> documents, ProjectId projectId) =>
        [.. documents
            .Where(document => document.ProjectId == projectId)
            .Select(document => solution.GetDocument(document)?.FilePath)
            .OfType<string>()];

    private static async Task<Solution> FixProjectAsync(
        Solution solution,
        ProjectId projectId,
        HashSet<string> scope,
        FixRequest request,
        List<string> unfixed,
        CancellationToken cancellationToken)
    {
        var attempted = new HashSet<string>(StringComparer.Ordinal);

        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var pending = await PendingAsync(solution, projectId, scope, request, attempted, cancellationToken).ConfigureAwait(false);

            if (pending.IsEmpty)
                return solution;

            var identifier = pending[0].Id;
            var reported = unfixed.Count;

            attempted.Add(identifier);
            solution = await FixAsync(solution, projectId, pending, unfixed, cancellationToken).ConfigureAwait(false);

            if (unfixed.Count == reported)
                await ReportResidueAsync(solution, projectId, scope, request, identifier, unfixed, cancellationToken).ConfigureAwait(false);
        }

        await ReportCapAsync(solution, projectId, scope, request, attempted, unfixed, cancellationToken).ConfigureAwait(false);

        return solution;
    }

    private static async Task<ImmutableArray<Diagnostic>> PendingAsync(
        Solution solution,
        ProjectId projectId,
        HashSet<string> scope,
        FixRequest request,
        HashSet<string> attempted,
        CancellationToken cancellationToken)
    {
        var project = solution.GetProject(projectId);

        if (project is null)
            return [];

        var diagnostics = await ProjectDiagnostics.OfProjectAsync(project, cancellationToken).ConfigureAwait(false);

        var candidates = diagnostics
            .Where(diagnostic => !attempted.Contains(diagnostic.Id) && request.Wants(diagnostic) && InScope(scope, diagnostic))
            .GroupBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        return candidates is null ? [] : [.. candidates];
    }

    private static bool InScope(HashSet<string> scope, Diagnostic diagnostic) =>
        Owner(diagnostic) is { Length: > 0 } file && scope.Any(path => PathBoundary.SameFile(path, file));

    private static string? Owner(Diagnostic diagnostic) => diagnostic.Location.SourceTree?.FilePath;

    private static async Task<Solution> FixAsync(
        Solution solution,
        ProjectId projectId,
        ImmutableArray<Diagnostic> pending,
        List<string> unfixed,
        CancellationToken cancellationToken)
    {
        var identifier = pending[0].Id;
        var project = solution.GetProject(projectId);
        var document = project?.Documents.FirstOrDefault(candidate => PathBoundary.SameFile(candidate.FilePath, Owner(pending[0])));
        var provider = project is null ? null : FixerCatalog.For(project).For(identifier);

        if (document is null || provider is null)
            return Skip(solution, unfixed, identifier, pending.Length, "no code fix provider is registered for it");

        return provider.GetFixAllProvider() is { } fixAll
            ? await OfferAsync(solution, provider, fixAll, document, pending, unfixed, cancellationToken).ConfigureAwait(false)
            : Skip(solution, unfixed, identifier, pending.Length, "the fixer has no FixAllProvider");
    }

    private static async Task<Solution> OfferAsync(
        Solution solution,
        CodeFixProvider provider,
        FixAllProvider fixAll,
        Document document,
        ImmutableArray<Diagnostic> pending,
        List<string> unfixed,
        CancellationToken cancellationToken)
    {
        var identifier = pending[0].Id;

        try
        {
            var action = await OfferedAsync(provider, document, pending[0], cancellationToken).ConfigureAwait(false);

            if (action is null)
                return Skip(solution, unfixed, identifier, pending.Length, "the fixer offered no action here");

            var batch = await fixAll.GetFixAsync(Context(provider, document, action, pending, cancellationToken)).ConfigureAwait(false);

            return batch is null
                ? Skip(solution, unfixed, identifier, pending.Length, "the fixer produced no batch fix")
                : await ChangedAsync(batch, solution, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Skip(solution, unfixed, identifier, pending.Length, "the fixer threw " + exception.GetType().Name);
        }
    }

    private static async Task<Solution> ChangedAsync(CodeAction action, Solution solution, CancellationToken cancellationToken)
    {
        var operations = await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false);

        return operations.OfType<ApplyChangesOperation>().FirstOrDefault()?.ChangedSolution ?? solution;
    }

    private static async Task<CodeAction?> OfferedAsync(
        CodeFixProvider provider,
        Document document,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        CodeAction? offered = null;
        var context = new CodeFixContext(document, diagnostic, (action, _) => offered ??= action, cancellationToken);

        await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);

        return offered;
    }

    private static Solution Skip(Solution solution, List<string> unfixed, string identifier, int count, string reason)
    {
        unfixed.Add(Line(identifier, count, reason));

        return solution;
    }

    private sealed class SuppliedDiagnostics(ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);

        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
            Task.FromResult(diagnostics.Where(diagnostic => PathBoundary.SameFile(document.FilePath, Owner(diagnostic))));

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>([]);
    }
    private static async Task ReportResidueAsync(
        Solution solution,
        ProjectId projectId,
        HashSet<string> scope,
        FixRequest request,
        string identifier,
        List<string> unfixed,
        CancellationToken cancellationToken)
    {
        var residue = await MatchingAsync(solution, projectId, scope, request, identifier, cancellationToken).ConfigureAwait(false);

        if (residue > 0)
            unfixed.Add(Line(identifier, residue, "the fixer left them unchanged"));
    }

    private static async Task ReportCapAsync(
        Solution solution,
        ProjectId projectId,
        HashSet<string> scope,
        FixRequest request,
        HashSet<string> attempted,
        List<string> unfixed,
        CancellationToken cancellationToken)
    {
        var pending = await PendingAsync(solution, projectId, scope, request, attempted, cancellationToken).ConfigureAwait(false);

        if (!pending.IsEmpty)
            unfixed.Add(Line(pending[0].Id, pending.Length, "the pass cap of 25 diagnostic ids was reached; run cleanup again or narrow it with ids="));
    }

    private static async Task<int> MatchingAsync(
        Solution solution,
        ProjectId projectId,
        HashSet<string> scope,
        FixRequest request,
        string identifier,
        CancellationToken cancellationToken)
    {
        var project = solution.GetProject(projectId);

        if (project is null)
            return 0;

        var diagnostics = await ProjectDiagnostics.OfProjectAsync(project, cancellationToken).ConfigureAwait(false);

        return diagnostics.Count(diagnostic =>
            string.Equals(diagnostic.Id, identifier, StringComparison.Ordinal)
            && request.Wants(diagnostic)
            && InScope(scope, diagnostic));
    }

    private static string Line(string identifier, int count, string reason) =>
        string.Create(CultureInfo.InvariantCulture, $"UNFIXED {identifier} x{count} - {reason}");

    private static FixAllContext Context(
        CodeFixProvider provider,
        Document document,
        CodeAction action,
        ImmutableArray<Diagnostic> pending,
        CancellationToken cancellationToken) =>
        new(
            document,
            provider,
            FixAllScope.Project,
            action.EquivalenceKey,
            [pending[0].Id],
            new SuppliedDiagnostics(pending),
            cancellationToken);
}

public readonly record struct FixOutcome(Solution Solution, IReadOnlyList<string> Unfixed);
