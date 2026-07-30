using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class EditGate
{
    public static async Task<Result<string>> ApplyAsync(
        LoadedWorkspace workspace,
        Solution updated,
        IReadOnlyList<DocumentId> changed,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var diff = await DiffAsync(workspace.Solution, updated, changed, cancellationToken).ConfigureAwait(false);

        if (options.DryRun)
            return Result.Ok(Render(options.Tool, diff, "dryRun"));

        var regression = options.AllowErrors
            ? []
            : await NewErrorsAsync(workspace.Solution, updated, changed, cancellationToken).ConfigureAwait(false);

        if (regression.Length > 0)
            return Result.Fail<string>(Errors.CompileRegression(regression));

        return workspace.TryApply(updated)
            ? Result.Ok(Render(options.Tool, diff, "applied"))
            : Result.Fail<string>(Errors.EditConflict("the workspace rejected the change"));
    }

    private static string Render(string tool, DocumentDiff[] diffs, string state)
    {
        var response = new ResponseBuilder(tool, state);

        response.Summary(diffs.Length, diffs.Length, "files changed");

        foreach (var diff in diffs)
            response.Line(diff.Text).Line(string.Create(CultureInfo.InvariantCulture, $"changedLines={diff.ChangedLines}"));

        return response.ToString();
    }

    private static async Task<DocumentDiff[]> DiffAsync(
        Solution before,
        Solution after,
        IReadOnlyList<DocumentId> changed,
        CancellationToken cancellationToken)
    {
        var diffs = new List<DocumentDiff>(changed.Count);

        foreach (var id in changed)
        {
            var diff = await DocumentDiff.CreateAsync(before, after, id, cancellationToken).ConfigureAwait(false);

            if (diff is not null)
                diffs.Add(diff);
        }

        return [.. diffs];
    }

    private static async Task<string[]> NewErrorsAsync(
        Solution before,
        Solution after,
        IReadOnlyList<DocumentId> changed,
        CancellationToken cancellationToken)
    {
        var projects = changed.Select(id => id.ProjectId).Distinct().ToArray();
        var baseline = await ErrorsAsync(before, projects, cancellationToken).ConfigureAwait(false);
        var current = await ErrorsAsync(after, projects, cancellationToken).ConfigureAwait(false);

        return [.. current.Where(entry => Appeared(baseline, entry)).Select(entry => entry.Key).Take(10)];
    }

    private static bool Appeared(Dictionary<string, int> baseline, KeyValuePair<string, int> entry) =>
        entry.Value > baseline.GetValueOrDefault(entry.Key);

    private static async Task<Dictionary<string, int>> ErrorsAsync(
        Solution solution,
        IReadOnlyList<ProjectId> projects,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var projectId in projects)
        {
            var compilation = await Compile(solution, projectId, cancellationToken).ConfigureAwait(false);

            if (compilation is null)
                continue;

            foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken))
                Collect(errors, diagnostic);
        }

        return errors;
    }

    private static Task<Compilation?> Compile(Solution solution, ProjectId projectId, CancellationToken cancellationToken)
    {
        var project = solution.GetProject(projectId);

        return project is null ? Task.FromResult<Compilation?>(null) : project.GetCompilationAsync(cancellationToken);
    }

    private static void Collect(Dictionary<string, int> errors, Diagnostic diagnostic)
    {
        if (diagnostic.Severity is not DiagnosticSeverity.Error)
            return;

        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{diagnostic.Id} {diagnostic.Location.GetLineSpan().Path}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}");

        errors[key] = errors.GetValueOrDefault(key) + 1;
    }
}
