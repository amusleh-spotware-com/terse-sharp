using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public readonly record struct GateRequest(string? Path, bool Changed, bool DryRun, bool Verbose);

public static class GateService
{
    public static async Task<Result<string>> RunAsync(
        LoadedWorkspace workspace,
        GateRequest request,
        CancellationToken cancellationToken)
    {
        var before = await FindingsAsync(workspace, request, cancellationToken).ConfigureAwait(false);

        if (!before.IsOk)
            return Result.Fail<string>(before.Error!);

        var formatted = await StepAsync(workspace, request, FixMode.None, "format", cancellationToken).ConfigureAwait(false);
        var cleaned = await StepAsync(workspace, request, FixMode.All, "cleanup", cancellationToken).ConfigureAwait(false);
        var after = await FindingsAsync(workspace, request, cancellationToken).ConfigureAwait(false);

        return after.IsOk
            ? Result.Ok(Render(request, before.Value!, after.Value!, formatted, cleaned))
            : Result.Fail<string>(after.Error!);
    }

    private static Task<Result<string[]>> FindingsAsync(
        LoadedWorkspace workspace,
        GateRequest request,
        CancellationToken cancellationToken) => AnalysisService.FindingsAsync(
        workspace,
        request.Path,
        DiagnosticSeverity.Info,
        includeDeadCode: true,
        request.Changed,
        cancellationToken);

    private static Task<Result<string>> StepAsync(
        LoadedWorkspace workspace,
        GateRequest request,
        FixMode mode,
        string tool,
        CancellationToken cancellationToken) => FormatService.RunAsync(
        workspace,
        new FixScope(request.Path, request.Changed),
        new FixRequest(mode, [], DiagnosticSeverity.Info, request.DryRun),
        new EditOptions(tool, DryRun: false, AllowErrors: false, request.Verbose),
        cancellationToken);

    private static string Render(
        GateRequest request,
        string[] before,
        string[] after,
        Result<string> formatted,
        Result<string> cleaned)
    {
        var response = new ResponseBuilder("gate", Scope(request)).Verbose(request.Verbose);
        var quiet = Quiet(formatted) && Quiet(cleaned);
        var clean = after.Length is 0 && formatted.IsOk && cleaned.IsOk && (!request.DryRun || quiet);

        response.Line(string.Create(
            CultureInfo.InvariantCulture,
            $"{(clean ? "clean" : "FAILED")}  analyzed={before.Length} fixed={Math.Max(before.Length - after.Length, 0)} remaining={after.Length}{(request.DryRun ? "  dryRun" : string.Empty)}"));

        if (clean && quiet && !request.Verbose)
            return response.ToString();

        Step(response, "format", formatted);
        Step(response, "cleanup", cleaned);

        foreach (var line in after)
            response.Line(line);

        return response.ToString();
    }

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
}
