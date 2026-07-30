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
        var found = new List<Diagnostic>();

        foreach (var project in workspace.Solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

            if (compilation is null)
                continue;

            found.AddRange(compilation.GetDiagnostics(cancellationToken).Where(diagnostic => Keep(diagnostic, path, minimum)));
        }

        return Render(path, found, maxResults);
    }

    private static bool Keep(Diagnostic diagnostic, string? path, DiagnosticSeverity minimum) =>
        diagnostic.Severity >= minimum
        && !diagnostic.IsSuppressed
        && (path is null || InFile(diagnostic, path));

    private static bool InFile(Diagnostic diagnostic, string path) =>
        diagnostic.Location.GetLineSpan().Path.EndsWith(
            Path.GetFileName(path),
            StringComparison.OrdinalIgnoreCase);

    private static string Render(string? path, List<Diagnostic> found, int maxResults)
    {
        var deduplicated = found
            .GroupBy(Key, StringComparer.Ordinal)
            .Select(group => new { Text = group.Key, Count = group.Count() })
            .OrderBy(entry => entry.Text, StringComparer.Ordinal)
            .ToArray();

        var response = new ResponseBuilder("get_diagnostics", path ?? "solution");

        response.Summary(Math.Min(maxResults, deduplicated.Length), deduplicated.Length, "diagnostics");

        foreach (var entry in deduplicated.Take(maxResults))
            response.Line(entry.Count is 1 ? entry.Text : string.Create(CultureInfo.InvariantCulture, $"{entry.Text} x{entry.Count}"));

        return response.ToString();
    }

    private static string Key(Diagnostic diagnostic) => string.Create(
        CultureInfo.InvariantCulture,
        $"{diagnostic.Id} {Severity(diagnostic)} {PositionFormat.Describe(diagnostic.Location)}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}");

    private static string Severity(Diagnostic diagnostic) => diagnostic.Severity.ToString().ToLowerInvariant();
}
