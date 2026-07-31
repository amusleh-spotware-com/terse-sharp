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

        return Render(workspace.Root, path, found, maxResults);
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

    private static string Render(string root, string? path, ConcurrentBag<Diagnostic> found, int maxResults)
    {
        var deduplicated = found
            .GroupBy(diagnostic => Key(root, diagnostic), StringComparer.Ordinal)
            .Select(group => new { Text = group.Key, Count = group.Count() })
            .OrderBy(entry => entry.Text, StringComparer.Ordinal)
            .ToArray();

        var response = new ResponseBuilder("get_diagnostics", path ?? "solution");

        response.Summary(Math.Min(maxResults, deduplicated.Length), deduplicated.Length, "diagnostics");

        foreach (var entry in deduplicated.Take(maxResults))
            response.Line(entry.Count is 1 ? entry.Text : string.Create(CultureInfo.InvariantCulture, $"{entry.Text} x{entry.Count}"));

        return response.ToString();
    }

    private static string Key(string root, Diagnostic diagnostic) => string.Create(
        CultureInfo.InvariantCulture,
        $"{diagnostic.Id} {Severity(diagnostic)} {PositionFormat.Describe(root, diagnostic.Location)}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}");

    private static string Severity(Diagnostic diagnostic) => diagnostic.Severity.ToString().ToLowerInvariant();
}
