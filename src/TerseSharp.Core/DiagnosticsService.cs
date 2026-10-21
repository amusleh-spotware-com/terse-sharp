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

        var split = request.Touched is { } touched ? BaselineFold.Split(found, touched) : new BaselineSplit([.. found], 0, 0, 0, string.Empty);
        var declaration = await DiagnosticDeclarations.ResolverAsync(split.Kept, cancellationToken).ConfigureAwait(false);

        return Render(workspace.Root, request, split, declaration, workspace.Load.Mismatched.Count);
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

        var reported = await SuppressedDiagnostics.CompilerAsync(project, compilation, cancellationToken).ConfigureAwait(false);

        foreach (var diagnostic in reported.Where(candidate => Keep(candidate, scope, minimum)))
            found.Add(diagnostic);
    }

    private static bool Keep(Diagnostic diagnostic, DiagnosticScope scope, DiagnosticSeverity minimum) =>
        diagnostic.Severity >= minimum
        && !diagnostic.IsSuppressed
        && scope.Includes(diagnostic);

    private static string Render(
        string root,
        DiagnosticsRequest request,
        BaselineSplit split,
        Func<Location, string?> declaration,
        int mismatched)
    {
        var found = split.Kept;
        var deduplicated = DiagnosticFold.Lines(root, found, Head, declaration);

        var response = new ResponseBuilder("get_diagnostics", request.Path ?? "solution");

        response.Summary(ResultCap.Shown(deduplicated.Length, request.MaxResults), deduplicated.Length, "diagnostics");

        if (found.Length > deduplicated.Length)
            response.Note(Occurrences(found));

        if (request.Touched is not null)
            AppendBaseline(response, split, request.BaseRef);

        if (mismatched > 0)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"config-mapping {mismatched} project(s) load at a different Configuration|Platform than the solution builds them - severities here follow the loaded one, build follows the solution; load_workspace names them"));

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

    private static void AppendBaseline(ResponseBuilder response, BaselineSplit split, string baseRef)
    {
        response.Note(string.Create(CultureInfo.InvariantCulture, $"{split.PreExisting} pre-existing diagnostic(s) were not reported - they sit outside every line the working tree changed against {baseRef} - drop baseRef= to list them"));

        if (BaselineFold.UnchangedErrorNote(split) is { } unchanged)
            response.Note(unchanged);

        if (BaselineFold.FilelessNote(split, baseRef) is { } fileless)
            response.Note(fileless);
    }
}

public readonly record struct DiagnosticsRequest(string? Path, DiagnosticSeverity Minimum, int MaxResults, TouchedLines? Touched = null, string BaseRef = "");
