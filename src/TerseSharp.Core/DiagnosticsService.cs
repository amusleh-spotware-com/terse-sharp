using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class DiagnosticsService
{
    public static async Task<string> CollectAsync(LoadedWorkspace workspace, DiagnosticsRequest request, CancellationToken cancellationToken)
    {
        var scope = DiagnosticScope.For(workspace, request.Path);
        var found = new ConcurrentBag<Diagnostic>();

        await Parallel.ForEachAsync(
            workspace.Solution.Projects,
            ParallelWork.Options(cancellationToken),
            (project, token) => CollectAsync(project, scope, request.Minimum, found, token)).ConfigureAwait(false);

        var kept = request.Touched is { } touched ? found.Where(touched.Covers).ToArray() : [.. found];
        var declaration = await DiagnosticDeclarations.ResolverAsync(kept, cancellationToken).ConfigureAwait(false);

        return Render(workspace.Root, request, kept, declaration, found.Count - kept.Length);
    }

    private static async ValueTask CollectAsync(
        Project project,
        DiagnosticScope scope,
        DiagnosticSeverity minimum,
        ConcurrentBag<Diagnostic> found,
        CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

        if (compilation is null)
            return;

        foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken).Where(candidate => Keep(candidate, scope, minimum)))
            found.Add(diagnostic);
    }

    private static bool Keep(Diagnostic diagnostic, DiagnosticScope scope, DiagnosticSeverity minimum) =>
        diagnostic.Severity >= minimum
        && !diagnostic.IsSuppressed
        && scope.Includes(diagnostic);

    private static string Render(
            string root,
            DiagnosticsRequest request,
            Diagnostic[] found,
            Func<Location, string?> declaration,
            int preExisting)
    {
        var deduplicated = DiagnosticFold.Lines(root, found, Head, declaration);

        var response = new ResponseBuilder("get_diagnostics", request.Path ?? "solution");

        response.Summary(ResultCap.Shown(deduplicated.Length, request.MaxResults), deduplicated.Length, "diagnostics");

        if (found.Length > deduplicated.Length)
            response.Note(Occurrences(found));

        if (request.Touched is not null)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"{preExisting} pre-existing diagnostic(s) were not reported - they sit outside every line the working tree changed against {request.BaseRef}"));

        foreach (var line in deduplicated.Capped(request.MaxResults))
            response.Line(line);

        return response.ToString();
    }

    private static string Head(Diagnostic diagnostic) => string.Create(
        CultureInfo.InvariantCulture,
        $"{diagnostic.Id} {Severity(diagnostic)}");

    private static string Severity(Diagnostic diagnostic) => diagnostic.Severity.ToString().ToLowerInvariant();

    private static string Occurrences(Diagnostic[] found)
    {
        var errors = 0;
        var warnings = 0;

        foreach (var diagnostic in found)
        {
            errors += diagnostic.Severity is DiagnosticSeverity.Error ? 1 : 0;
            warnings += diagnostic.Severity is DiagnosticSeverity.Warning ? 1 : 0;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"occurrences={found.Length} errors={errors} warnings={warnings} - a line folds every position sharing its id and message; an edit's errors= counts the same way over the projects it touched");
    }
}

public readonly record struct DiagnosticsRequest(string? Path, DiagnosticSeverity Minimum, int MaxResults, TouchedLines? Touched = null, string BaseRef = "");
