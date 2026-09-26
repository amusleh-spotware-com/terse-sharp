using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public readonly record struct GateRequest(string? Path, bool Changed, bool DryRun, bool Verbose, TouchedLines? Touched = null);

public static class GateService
{
    public static async Task<Result<string>> RunAsync(
        LoadedWorkspace workspace,
        GateRequest request,
        CancellationToken cancellationToken)
    {
        var analyzed = Analyzed(workspace, request);
        var before = await FindingsAsync(workspace, request, cancellationToken).ConfigureAwait(false);

        if (!before.IsOk)
            return Result.Fail<string>(before.Error!);

        var formatted = await StepAsync(workspace, request, FixMode.None, "format", cancellationToken).ConfigureAwait(false);
        var cleaned = await StepAsync(workspace, request, FixMode.All, "cleanup", cancellationToken).ConfigureAwait(false);
        var after = await FindingsAsync(workspace, request, cancellationToken).ConfigureAwait(false);

        return after.IsOk
            ? Result.Ok(Render(request, analyzed, before.Value.Reported.Count, after.Value, formatted, cleaned))
            : Result.Fail<string>(after.Error!);
    }

    private static Task<Result<GateFindings>> FindingsAsync(
        LoadedWorkspace workspace,
        GateRequest request,
        CancellationToken cancellationToken) => AnalysisService.FindingsAsync(
        workspace,
        request.Path,
        DiagnosticSeverity.Info,
        includeDeadCode: true,
        request.Changed,
        request.Touched,
        cancellationToken);

    private static Task<Result<string>> StepAsync(
        LoadedWorkspace workspace,
        GateRequest request,
        FixMode mode,
        string tool,
        CancellationToken cancellationToken) => FormatService.RunAsync(
    workspace,
    new FixScope(request.Path, request.Changed, request.Touched),
    new FixRequest(mode, [], DiagnosticSeverity.Info, request.DryRun),
    new EditOptions(tool, DryRun: false, AllowErrors: false, request.Verbose),
    cancellationToken);

    private static string Render(
        GateRequest request,
        int analyzed,
        int before,
        GateFindings after,
        Result<string> formatted,
        Result<string> cleaned)
    {
        var response = new ResponseBuilder("gate", Scope(request)).Verbose(request.Verbose);
        var quiet = Quiet(formatted) && Quiet(cleaned);
        var clean = after.Reported.Count is 0 && formatted.IsOk && cleaned.IsOk && (!request.DryRun || quiet);

        response.Line(string.Create(
            CultureInfo.InvariantCulture,
            $"{(clean ? "clean" : "FAILED")}  analyzed={analyzed} fixed={Math.Max(before - after.Reported.Count, 0)} remaining={after.Reported.Count}{PreExisting(request, after)}{(request.DryRun ? "  dryRun" : string.Empty)}"));

        if (clean && quiet && !request.Verbose)
            return response.ToString();

        Step(response, "format", formatted);
        Step(response, "cleanup", cleaned);

        foreach (var line in after.Reported)
            response.Line(line);

        return response.ToString();
    }

    private static string PreExisting(GateRequest request, GateFindings after) =>
        request.Touched is null || after.PreExisting is 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"  preExisting={after.PreExisting}");

    private static bool Quiet(Result<string> result) =>
        result.IsOk
        && (string.Equals(result.Value, "clean", StringComparison.Ordinal)
            || result.Value!.StartsWith("0 files changed", StringComparison.Ordinal));

    private static string Scope(GateRequest request) =>
        request.Path ?? (request.Changed ? "changed" : "solution");

    private static void Step(ResponseBuilder response, string step, Result<string> result)
    {
        var text = result.IsOk ? result.Value! : result.Error!.Render();

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            response.Note(step + ": " + line);
    }

    private static int Analyzed(LoadedWorkspace workspace, GateRequest request) =>
        request.Path is null && !request.Changed
            ? DocumentScope.Editable(workspace).Count()
            : DocumentScope.Select(workspace, request.Path, request.Changed).Length;
}

public readonly record struct GateFindings(IReadOnlyList<string> Reported, int PreExisting);
