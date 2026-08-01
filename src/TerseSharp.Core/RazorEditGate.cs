using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

public static class RazorEditGate
{
    public static async Task<Result<string>> ApplyAsync(
        RazorContext context,
        string updatedText,
        RazorEditOptions options,
        CancellationToken cancellationToken)
    {
        var reparsed = RazorDocument.Parse(context.FullPath, updatedText);

        if (reparsed.Issues.Count > context.Document.Issues.Count)
        {
            return Result.Fail<string>(Errors.Invalid(
                "the edit would leave markup the Razor compiler cannot parse: " + reparsed.Issues[^1],
                "fix the markup you are inserting, or edit the file with edit_text deliberately"));
        }

        var report = options.AllowErrors
            ? null
            : await AnalyseAsync(context, updatedText, cancellationToken).ConfigureAwait(false);

        if (options.DryRun)
            return Result.Ok(Render(context, updatedText, options.Tool, "dryRun", report, true));

        if (report is { NewErrors.Count: > 0 })
            return Result.Fail<string>(Errors.CompileRegression([.. report.NewErrors]));

        AtomicWrite.Text(context.FullPath, updatedText);
        RazorIndex.Invalidate(context.FullPath);

        return Result.Ok(Render(context, updatedText, options.Tool, "applied", report, Refresh(context, updatedText)));
    }

    private static bool Refresh(RazorContext context, string updatedText)
    {
        if (AdditionalId(context) is not { } id)
            return false;

        return context.Workspace.TryApply(context.Workspace.Solution.WithAdditionalDocumentText(id, SourceText.From(updatedText)));
    }

    private static DocumentId? AdditionalId(RazorContext context) => context
        .Project
        ?.AdditionalDocuments
        .FirstOrDefault(document => PathBoundary.SameFile(document.FilePath, context.FullPath))
        ?.Id;

    private static async Task<RazorGateReport?> AnalyseAsync(
        RazorContext context,
        string updatedText,
        CancellationToken cancellationToken)
    {
        if (AdditionalId(context) is not { } id || context.Project is null)
            return null;

        var clock = Stopwatch.StartNew();
        var before = await TallyAsync(context.Project, cancellationToken).ConfigureAwait(false);
        var updated = context.Workspace.Solution.WithAdditionalDocumentText(id, SourceText.From(updatedText));
        var project = updated.GetProject(context.Project.Id);

        if (project is null)
            return null;

        var after = await TallyAsync(project, cancellationToken).ConfigureAwait(false);

        return new RazorGateReport(
            [.. after.Errors.Where(entry => !before.Errors.Contains(entry)).Take(10)],
            after.ErrorCount,
            after.ErrorCount - before.ErrorCount,
            after.WarningCount,
            after.WarningCount - before.WarningCount,
            clock.ElapsedMilliseconds);
    }

    private static async Task<Tally> TallyAsync(Project project, CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

        if (compilation is null)
            return new Tally([], 0, 0);

        var diagnostics = compilation.GetDiagnostics(cancellationToken);

        return new Tally(
            [.. diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error).Select(Describe)],
            diagnostics.Count(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error),
            diagnostics.Count(diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning));
    }

    private static string Describe(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetMappedLineSpan();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{diagnostic.Id} {Path.GetFileName(span.Path)}:{span.StartLinePosition.Line + 1}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}");
    }

    private static string Render(
        RazorContext context,
        string updatedText,
        string tool,
        string state,
        RazorGateReport? report,
        bool refreshed)
    {
        var response = new ResponseBuilder(tool, state);

        response.Summary(1, 1, "files changed");
        response.Note(Announce(report));

        if (!refreshed)
            response.Note("workspace=stale - the file changed on disk; call load_workspace again before asking for its symbols");

        if (report is { NewErrors.Count: > 0 })
            Warn(response, report);

        response.Line(UnifiedDiff.Between(context.Relative, context.Document.Text, updatedText));
        response.Line(string.Create(
            CultureInfo.InvariantCulture,
            $"changedLines={UnifiedDiff.ChangedLines(context.Document.Text, updatedText)}"));

        return response.ToString();
    }

    private static void Warn(ResponseBuilder response, RazorGateReport report)
    {
        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"WARNING this edit introduces {report.NewErrors.Count} new error(s) and would be rolled back"));

        foreach (var error in report.NewErrors)
            response.Note(error);
    }

    private static string Announce(RazorGateReport? report) => report is null
        ? "compileGate=unavailable - the file is not an additional document of a loaded project, so the Razor generator could not re-run"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"errors={report.Errors} ({Signed(report.ErrorDelta)}) warnings={report.Warnings} ({Signed(report.WarningDelta)})  regenMs={report.RegenerationMs}");

    private static string Signed(int delta) =>
        delta >= 0 ? "+" + delta.ToString(CultureInfo.InvariantCulture) : delta.ToString(CultureInfo.InvariantCulture);

    private sealed record RazorGateReport(
        IReadOnlyList<string> NewErrors,
        int Errors,
        int ErrorDelta,
        int Warnings,
        int WarningDelta,
        long RegenerationMs);

    private readonly record struct Tally(IReadOnlyList<string> Errors, int ErrorCount, int WarningCount);
}

public sealed record RazorEditOptions(string Tool, bool DryRun, bool AllowErrors);
