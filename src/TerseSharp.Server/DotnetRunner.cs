using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TerseSharp.Server;

public static partial class DotnetRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public static async Task<string> BuildAsync(
        LoadedWorkspace workspace,
        string? project,
        CancellationToken cancellationToken)
    {
        var target = project ?? workspace.SolutionPath;
        var run = await RunAsync($"build \"{target}\" -nodeReuse:false -v q --nologo", workspace.Root, cancellationToken)
            .ConfigureAwait(false);

        return RenderBuild(target, run);
    }

    public static async Task<string> TestAsync(
        LoadedWorkspace workspace,
        string? project,
        string? filter,
        CancellationToken cancellationToken)
    {
        var target = project ?? workspace.SolutionPath;
        var arguments = string.IsNullOrWhiteSpace(filter)
            ? $"test \"{target}\" -nodeReuse:false --nologo"
            : $"test \"{target}\" -nodeReuse:false --nologo --filter \"{filter}\"";

        var run = await RunAsync(arguments, workspace.Root, cancellationToken).ConfigureAwait(false);

        return RenderTest(target, run);
    }

    private static string RenderBuild(string target, ProcessRun run)
    {
        var diagnostics = DiagnosticLine()
            .Matches(run.Output)
            .Select(match => match.Value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var response = new ResponseBuilder("build", target);

        response.Summary(diagnostics.Length, diagnostics.Length, "diagnostics");
        response.Note(string.Create(CultureInfo.InvariantCulture, $"exitCode={run.ExitCode} elapsedMs={run.ElapsedMilliseconds}"));

        foreach (var diagnostic in diagnostics)
            response.Line(diagnostic);

        return response.ToString();
    }

    private static string RenderTest(string target, ProcessRun run)
    {
        var failures = FailureLine()
            .Matches(run.Output)
            .Select(match => match.Value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var response = new ResponseBuilder("run_tests", target);

        response.Summary(failures.Length, failures.Length, "failures");
        response.Note(string.Create(CultureInfo.InvariantCulture, $"exitCode={run.ExitCode} elapsedMs={run.ElapsedMilliseconds}"));
        response.Note(Summary(run.Output));

        foreach (var failure in failures)
            response.Line(failure);

        return response.ToString();
    }

    private static string Summary(string output)
    {
        var match = TestSummary().Match(output);

        return match.Success ? match.Value.Trim() : "no test summary found";
    }

    private static async Task<ProcessRun> RunAsync(string arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        deadline.CancelAfter(Timeout);

        var run = await DrainAsync(process, stopwatch, deadline.Token).ConfigureAwait(false);

        return run ?? Abandon(process, stopwatch);
    }

    private static async Task<ProcessRun?> DrainAsync(Process process, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var pending = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            var output = await pending.ConfigureAwait(false);
            var error = await errors.ConfigureAwait(false);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            return new ProcessRun(process.ExitCode, output + error, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static ProcessRun Abandon(Process process, Stopwatch stopwatch)
    {
        stopwatch.Stop();

        if (!process.HasExited)
            process.Kill(entireProcessTree: true);

        return new ProcessRun(
            -1,
            string.Create(CultureInfo.InvariantCulture, $"TIMED_OUT after {stopwatch.ElapsedMilliseconds} ms; the process was killed"),
            stopwatch.ElapsedMilliseconds);
    }

    [GeneratedRegex(@"^.*?: (error|warning) [A-Z]+\d+:.*$", RegexOptions.Multiline)]
    private static partial Regex DiagnosticLine();

    [GeneratedRegex(@"^\s*(Failed|Error Message:|Assert\.).*$", RegexOptions.Multiline)]
    private static partial Regex FailureLine();

    [GeneratedRegex(@"(Passed!|Failed!).*$", RegexOptions.Multiline)]
    private static partial Regex TestSummary();
}

internal sealed record ProcessRun(int ExitCode, string Output, long ElapsedMilliseconds);
