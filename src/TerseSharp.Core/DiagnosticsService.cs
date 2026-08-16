using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class DiagnosticsService
{
    public static async Task<string> CollectAsync(
        LoadedWorkspace workspace,
        string? path,
        DiagnosticSeverity minimum,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var scope = DiagnosticScope.For(workspace, path);
        var found = new ConcurrentBag<Diagnostic>();

        await Parallel.ForEachAsync(
            workspace.Solution.Projects,
            ParallelWork.Options(cancellationToken),
            (project, token) => CollectAsync(project, scope, minimum, found, token)).ConfigureAwait(false);

        var declaration = await DiagnosticDeclarations.ResolverAsync(found, cancellationToken).ConfigureAwait(false);

        return Render(workspace.Root, path, found, declaration, maxResults);
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
        string? path,
        ConcurrentBag<Diagnostic> found,
        Func<Location, string?> declaration,
        int maxResults)
    {
        var deduplicated = DiagnosticFold.Lines(root, found, Head, declaration);

        var response = new ResponseBuilder("get_diagnostics", path ?? "solution");

        response.Summary(ResultCap.Shown(deduplicated.Length, maxResults), deduplicated.Length, "diagnostics");

        foreach (var line in deduplicated.Capped(maxResults))
            response.Line(line);

        return response.ToString();
    }

    private static string Head(Diagnostic diagnostic) => string.Create(
        CultureInfo.InvariantCulture,
        $"{diagnostic.Id} {Severity(diagnostic)}");

    private static string Severity(Diagnostic diagnostic) => diagnostic.Severity.ToString().ToLowerInvariant();
}
