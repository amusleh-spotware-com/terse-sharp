using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TerseSharp.Server;

public static partial class DotnetRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    private const int MaxFailures = 20;

    private const int MaxMessageLines = 12;

    private const int MaxTestNames = 200;

    private const int MaxTimings = 50;

    public static async Task<string> BuildAsync(
        LoadedWorkspace workspace,
        string? project,
        CancellationToken cancellationToken)
    {
        var target = project ?? workspace.SolutionPath;
        var run = await RunAsync(["build", target, "-nodeReuse:false", "-v", "q", "--nologo"], workspace.Root, DefaultTimeout, cancellationToken)
            .ConfigureAwait(false);

        return RenderBuild(target, run);
    }

    internal static async Task<TestRunResult> TestAsync(
        LoadedWorkspace workspace,
        TestRunRequest request,
        CancellationToken cancellationToken)
    {
        var results = Directory.CreateTempSubdirectory("terse-tests-");

        try
        {
            var run = await RunAsync(Arguments(request, results.FullName), workspace.Root, request.Timeout, cancellationToken)
                .ConfigureAwait(false);
            var report = Report(results, workspace.Root);

            return new TestRunResult(RenderTest(run, report, request), report);
        }
        finally
        {
            Discard(results);
        }
    }

    private static void Discard(DirectoryInfo results)
    {
        try
        {
            results.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal static async Task<string> ListTestsAsync(
        LoadedWorkspace workspace,
        string target,
        string? contains,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var run = await RunAsync(["test", target, "-nodeReuse:false", "--nologo", "--list-tests"], workspace.Root, timeout, cancellationToken)
            .ConfigureAwait(false);

        return RenderTestNames(target, run, contains);
    }

    private static TestRunReport Report(DirectoryInfo results, string workspaceRoot) =>
        TestResultParser.Parse(Directory.EnumerateFiles(results.FullName, "*.trx", SearchOption.AllDirectories), workspaceRoot);

    private static string[] Arguments(TestRunRequest request, string resultsDirectory)
    {
        var arguments = new List<string>(12)
        {
            "test", request.Target, "-nodeReuse:false", "--nologo", "--logger", "trx", "--results-directory", resultsDirectory,
        };

        if (request.Filter is { Length: > 0 } filter)
            arguments.AddRange(["--filter", filter]);

        if (request.NoBuild)
            arguments.Add("--no-build");

        return [.. arguments];
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

        AppendLockWarning(response, run);

        foreach (var diagnostic in diagnostics)
            response.Line(diagnostic);

        AppendTail(response, run, diagnostics.Length);

        return response.ToString();
    }

    private static void AppendLockWarning(ResponseBuilder response, ProcessRun run)
    {
        if (run.ExitCode is 0 || !LockedOutput().IsMatch(run.Output))
            return;

        response.Note("WARNING a locked output file blocked the build; the loaded workspace holds MSBuild file locks");
        response.Note("remedy: unload_workspace, retry build, then load_workspace");
    }

    private static string RenderTest(ProcessRun run, TestRunReport report, TestRunRequest request)
    {
        if (report.Total is 0 && run.ExitCode is not 0)
            return RenderNoResults(request.Target, run);

        var shown = Math.Min(report.Failures.Length, MaxFailures);
        var response = new ResponseBuilder("run_tests", request.Target);

        response.Summary(shown, report.Failures.Length, "failures");
        response.Note(Counters(report, run));

        AppendWarnings(response, run, report, request.Filter);
        AppendFailures(response, report, shown);
        AppendTimings(response, report, request);

        return response.ToString();
    }

    private static string RenderNoResults(string target, ProcessRun run)
    {
        var response = new ResponseBuilder("run_tests", target);

        response.Note(run.TimedOut
            ? string.Create(CultureInfo.InvariantCulture, $"FAILED timed out after {run.ElapsedMilliseconds} ms, no test results were produced; last output lines:")
            : string.Create(CultureInfo.InvariantCulture, $"FAILED exitCode={run.ExitCode} elapsedMs={run.ElapsedMilliseconds}, no test results were produced; last output lines:"));

        foreach (var line in Tail(run.Output))
            response.Line(line);

        return response.ToString();
    }

    private static string RenderTestNames(string target, ProcessRun run, string? contains)
    {
        var names = TestNameList.Parse(run.Output, contains);
        var shown = Math.Min(names.Length, MaxTestNames);
        var response = new ResponseBuilder("list_tests", target);

        response.Summary(shown, names.Length, "tests");

        for (var index = 0; index < shown; index++)
            response.Line(names[index]);

        AppendTail(response, run, names.Length);

        return response.ToString();
    }

    private static string Counters(TestRunReport report, ProcessRun run) => string.Create(
        CultureInfo.InvariantCulture,
        $"passed={report.Passed} failed={report.Failed} skipped={report.Skipped} total={report.Total} durationMs={report.DurationMs} exitCode={run.ExitCode} elapsedMs={run.ElapsedMilliseconds}");

    private static void AppendWarnings(ResponseBuilder response, ProcessRun run, TestRunReport report, string? filter)
    {
        if (run.TimedOut)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"WARNING timed out after {run.ElapsedMilliseconds} ms; the results below are partial"));

        if (report.Total is 0)
            response.Note(NoMatch(filter));
    }

    private static string NoMatch(string? filter) => filter is { Length: > 0 }
        ? string.Create(CultureInfo.InvariantCulture, $"WARNING no test matched filter '{filter}'; this is not a green run")
        : "WARNING no test was discovered; this is not a green run";

    private static void AppendFailures(ResponseBuilder response, TestRunReport report, int shown)
    {
        for (var index = 0; index < shown; index++)
            AppendFailure(response, report.Failures[index]);
    }

    private static void AppendFailure(ResponseBuilder response, TestFailure failure)
    {
        response.Note(string.Empty);
        response.Note(string.Create(CultureInfo.InvariantCulture, $"FAIL {failure.Name} ({failure.DurationMs} ms)"));

        foreach (var line in MessageLines(failure.Message))
            response.Line("  " + line);

        if (failure.Frame is { Length: > 0 } frame)
            response.Line("  at " + frame);
    }

    private static void AppendTimings(ResponseBuilder response, TestRunReport report, TestRunRequest request)
    {
        if (request.IncludePassed)
            AppendLines(response, report.PassedTests, "PASS");

        if (request.Slowest > 0)
            AppendLines(response, [.. report.Slowest(request.Slowest)], "SLOW");
    }

    private static void AppendLines(ResponseBuilder response, IReadOnlyList<TestTiming> tests, string prefix)
    {
        if (tests.Count is 0)
            return;

        var shown = Math.Min(tests.Count, MaxTimings);

        response.Note(string.Empty);

        for (var index = 0; index < shown; index++)
            response.Line(string.Create(CultureInfo.InvariantCulture, $"{prefix} {tests[index].Name} ({tests[index].DurationMs} ms)"));

        if (shown < tests.Count)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"{prefix} truncated=true, total={tests.Count}"));
    }

    private static IEnumerable<string> MessageLines(string message) =>
        message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(MaxMessageLines);

    private static void AppendTail(ResponseBuilder response, ProcessRun run, int parsed)
    {
        if (run.ExitCode is 0 || parsed > 0)
            return;

        response.Note("FAILED with no parsable diagnostics; last output lines:");

        foreach (var line in Tail(run.Output))
            response.Line(line);
    }

    private static IEnumerable<string> Tail(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(15);

    private static async Task<ProcessRun> RunAsync(string[] arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        deadline.CancelAfter(timeout);

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
            string.Create(CultureInfo.InvariantCulture, $"TIMED_OUT after {stopwatch.ElapsedMilliseconds} ms; the process tree was killed"),
            stopwatch.ElapsedMilliseconds,
            TimedOut: true);
    }

    [GeneratedRegex(@"^.*?: (error|warning) [A-Z]+\d+:.*$", RegexOptions.Multiline)]
    private static partial Regex DiagnosticLine();

    [GeneratedRegex(@"MSB3021|MSB3027|being used by another process", RegexOptions.IgnoreCase)]
    private static partial Regex LockedOutput();
}

internal sealed record ProcessRun(int ExitCode, string Output, long ElapsedMilliseconds, bool TimedOut = false);

internal readonly record struct TestRunResult(string Response, TestRunReport Report);
