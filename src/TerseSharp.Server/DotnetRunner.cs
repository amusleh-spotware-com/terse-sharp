using System.Text.RegularExpressions;

namespace TerseSharp.Server;

public static partial class DotnetRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    private const int MaxFailures = 20;

    private const int MaxMessageLines = 30;

    private const int MaxTestNames = 200;

    private const int MaxTimings = 50;

    private const int MaxTailLines = 15;

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    public static async Task<BuildRun> BuildAsync(
        WorkspaceTarget workspace,
        string? project,
        BuildScope scope,
        bool verbose,
        CancellationToken cancellationToken)
    {
        var target = project ?? workspace.SolutionPath;
        var arguments = scope.Applied(["build", target, "-nodeReuse:false", "-v", "q", "--nologo"]);
        var run = await RunAsync(arguments, workspace.Root, DefaultTimeout, cancellationToken)
            .ConfigureAwait(false);

        return new BuildRun(RenderBuild(target, workspace.Root, run, verbose), Locked(run));
    }
    private static bool Locked(ProcessRun run) => IsLockedOutput(run.ExitCode, run.Output);

    private static bool IsGreen(ProcessRun run, TestRunReport report) =>
        run.ExitCode is 0 && !run.TimedOut && report.Total > 0 && report.Failures.Length is 0;

    private static string QuietTest(TestRunReport report) => string.Create(
    CultureInfo.InvariantCulture,
    $"run_tests PASSED  passed={report.Passed} skipped={report.Skipped} total={report.Total} durationMs={report.DurationMs}") + PerProject(report);

    internal static bool IsLockedOutput(int exitCode, string output) =>
        exitCode is not 0 && LockedOutput().IsMatch(output);

    internal static async Task<TestRunResult> TestAsync(
        WorkspaceTarget workspace,
        TestRunRequest request,
        CancellationToken cancellationToken)
    {
        var results = Directory.CreateTempSubdirectory("terse-tests-");

        try
        {
            var (run, missing) = await InvokeAsync(workspace, request, results.FullName, cancellationToken).ConfigureAwait(false);
            var report = Report(results, workspace.Root);
            var notes = request.Verbose ? await NotesAsync(results, cancellationToken).ConfigureAwait(false) : [];
            var response = Noted(RenderTest(run, report, request, workspace.Root), notes);

            return new TestRunResult(TimedOut(response, missing, request.Invocations.Length), report, Locked(run));
        }
        finally
        {
            Discard(results);
        }
    }

    private static string TimedOut(string response, List<string> missing, int invocations) => missing.Count is 0
    ? response
    : string.Create(
        CultureInfo.InvariantCulture,
        $"{response}\nWARNING {Stopped(missing, invocations)}: {string.Join(", ", missing)}");

    private static string Stopped(List<string> missing, int invocations) => invocations is 1
        ? "this run timed out and produced no results"
        : string.Create(CultureInfo.InvariantCulture, $"the batch stopped at the first timeout; {missing.Count} of {invocations} project(s) produced no results");

    private static async Task<(ProcessRun Run, List<string> Missing)> InvokeAsync(
        WorkspaceTarget workspace,
        TestRunRequest request,
        string resultsDirectory,
        CancellationToken cancellationToken)
    {
        var combined = default(ProcessRun);
        var missing = new List<string>();
        var stopped = false;
        var environment = new[] { new KeyValuePair<string, string>(ResultsDirectoryVariable, resultsDirectory) };

        foreach (var target in request.Invocations)
        {
            if (stopped)
            {
                missing.Add(Path.GetFileNameWithoutExtension(target));

                continue;
            }

            var run = await RunAsync(
                Arguments(request with { Target = target }, resultsDirectory),
                workspace.Root,
                request.Timeout,
                cancellationToken,
                environment).ConfigureAwait(false);

            combined = combined is null ? run : Merge(combined, run);

            if (!run.TimedOut)
                continue;

            missing.Add(Path.GetFileNameWithoutExtension(target));
            stopped = true;
        }

        return (combined ?? new ProcessRun(0, string.Empty, 0), missing);
    }

    internal static ProcessRun Merge(ProcessRun first, ProcessRun next) => new(
        first.ExitCode is 0 ? next.ExitCode : first.ExitCode,
        first.Output + "\n" + next.Output,
        first.ElapsedMilliseconds + next.ElapsedMilliseconds,
        first.TimedOut || next.TimedOut,
        first.StandardOutput + "\n" + next.StandardOutput,
        first.StandardError + "\n" + next.StandardError);

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

    private static TestRunReport Report(DirectoryInfo results, string workspaceRoot) =>
            TestResultParser.Parse(Directory.EnumerateFiles(results.FullName, "*.trx", RecursiveAndTolerant), workspaceRoot);

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

        return request.Scope.Applied(arguments);
    }

    internal static string RenderBuild(string target, string root, ProcessRun run, bool verbose)
    {
        var diagnostics = Diagnostics(run.Output);

        if (!verbose && run.ExitCode is 0 && diagnostics.Errors.Length is 0)
            return QuietBuild(run, diagnostics.Warnings.Length);

        var shown = Shown(diagnostics, verbose);
        var response = new ResponseBuilder("build", target).Verbose(verbose);

        response.Summary(shown.Length, Parsed(diagnostics), "diagnostics");
        response.Note(string.Create(CultureInfo.InvariantCulture, $"exitCode={run.ExitCode} elapsedMs={run.ElapsedMilliseconds}"));

        if (verbose)
            AppendOutputs(response, run, root, target);

        AppendLockWarning(response, run);
        AppendHiddenWarnings(response, diagnostics, shown.Length);

        foreach (var diagnostic in shown)
            response.Line(Relative(diagnostic, root));

        AppendTail(response, run, diagnostics.Errors.Length, root);

        return response.ToString();
    }

    internal static string Relative(string line, string root) => root is { Length: > 0 }
        ? line.Replace(root + Path.DirectorySeparatorChar, string.Empty, StringComparison.OrdinalIgnoreCase)
        : line;

    private static void AppendLockWarning(ResponseBuilder response, ProcessRun run)
    {
        if (!Locked(run))
            return;

        response.Note("WARNING a locked output file blocked the operation; the loaded workspace holds MSBuild file locks");
        response.Note("remedy: see the NOTE below; the operation is retried automatically when one workspace is loaded");
    }

    private static string RenderTest(ProcessRun run, TestRunReport report, TestRunRequest request, string root)
    {
        if (report.Total is 0 && run.ExitCode is not 0)
            return RenderNoResults(request.Target, run, request.Verbose, root);

        if (IsGreen(run, report) && !request.WantsDetail)
            return QuietTest(report);

        var shown = Math.Min(report.Failures.Length, MaxFailures);
        var response = new ResponseBuilder("run_tests", request.Target).Verbose(request.Verbose);

        response.Summary(shown, report.Failures.Length, "failures");
        response.Note(Counters(report, run));

        AppendWarnings(response, run, report, request.Filter);
        AppendFailures(response, report, shown);
        AppendTimings(response, report, request);

        return response.ToString();
    }
    internal static string RenderNoResults(string target, ProcessRun run, bool verbose, string root = "")
    {
        var response = new ResponseBuilder("run_tests", target).Verbose(verbose);

        response.Note(run.TimedOut
            ? string.Create(CultureInfo.InvariantCulture, $"FAILED timed out after {run.ElapsedMilliseconds} ms, no test results were produced")
            : string.Create(CultureInfo.InvariantCulture, $"FAILED exitCode={run.ExitCode} elapsedMs={run.ElapsedMilliseconds}, no test results were produced"));

        AppendLockWarning(response, run);
        AppendFailureOutput(response, run, reported: 0, verbose, root);

        return response.ToString();
    }
    internal static string RenderTestNames(string target, ProcessRun run, string? contains, string root = "")
    {
        var names = TestNameList.Parse(run.Output, contains);
        var shown = Math.Min(names.Length, MaxTestNames);
        var response = new ResponseBuilder("list_tests", target);

        response.Summary(shown, names.Length, "tests");

        AppendLockWarning(response, run);

        for (var index = 0; index < shown; index++)
            response.Line(names[index]);

        AppendFailureOutput(response, run, names.Length, verbose: false, root);

        return response.ToString();
    }

    private static string Counters(TestRunReport report, ProcessRun run) => string.Create(
    CultureInfo.InvariantCulture,
    $"passed={report.Passed} failed={report.Failed} skipped={report.Skipped} total={report.Total} durationMs={report.DurationMs} exitCode={run.ExitCode} elapsedMs={run.ElapsedMilliseconds}") + PerProject(report);

    private static void AppendWarnings(ResponseBuilder response, ProcessRun run, TestRunReport report, string? filter)
    {
        AppendLockWarning(response, run);

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

    private static void AppendTail(ResponseBuilder response, ProcessRun run, int errors, string root)
    {
        if (run.ExitCode is 0 || errors > 0)
            return;

        response.Note("FAILED with no error-severity diagnostic; last output lines:");

        foreach (var line in Tail(run.Output))
            response.Line(Relative(line, root));
    }

    private static IEnumerable<string> Tail(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(MaxTailLines);

    private static Task<ProcessRun> RunAsync(
            string[] arguments,
            string workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            IReadOnlyList<KeyValuePair<string, string>>? environment = null) =>
            ChildProcess.RunAsync("dotnet", arguments, workingDirectory, timeout, cancellationToken, environment);

    [GeneratedRegex(@"^.*?: (error|warning) [A-Z]+\d+:.*$", RegexOptions.Multiline)]
    private static partial Regex DiagnosticLine();

    [GeneratedRegex(@"MSB3021|MSB3027|being used by another process", RegexOptions.IgnoreCase)]
    private static partial Regex LockedOutput();

    internal static async Task<BuildRun> ListTestNamesAsync(
        WorkspaceTarget workspace,
        string target,
        string? contains,
        BuildScope scope,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var arguments = scope.Applied(["test", target, "-nodeReuse:false", "--nologo", "--list-tests"]);
        var run = await RunAsync(arguments, workspace.Root, timeout, cancellationToken)
            .ConfigureAwait(false);

        return new BuildRun(RenderTestNames(target, run, contains, workspace.Root), Locked(run));
    }

    private static string QuietBuild(ProcessRun run, int warnings) => string.Create(
        CultureInfo.InvariantCulture,
        $"build ok  errors=0 warnings={warnings} emitted  elapsedMs={run.ElapsedMilliseconds}");

    internal static BuildDiagnostics Diagnostics(string output)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lookup = seen.GetAlternateLookup<ReadOnlySpan<char>>();

        foreach (var match in DiagnosticLine().EnumerateMatches(output))
        {
            var line = output.AsSpan(match.Index, match.Length).Trim();

            if (lookup.Contains(line))
                continue;

            var text = new string(line);

            seen.Add(text);
            (IsError(line) ? errors : warnings).Add(text);
        }

        return new BuildDiagnostics([.. errors], [.. warnings]);
    }

    private static bool IsError(ReadOnlySpan<char> line)
    {
        var error = line.IndexOf(": error ", StringComparison.Ordinal);
        var warning = line.IndexOf(": warning ", StringComparison.Ordinal);

        return error >= 0 && (warning < 0 || error < warning);
    }

    private static string[] Shown(BuildDiagnostics diagnostics, bool verbose) =>
        verbose || diagnostics.Errors.Length is 0
            ? [.. diagnostics.Errors, .. diagnostics.Warnings]
            : diagnostics.Errors;

    private static void AppendHiddenWarnings(ResponseBuilder response, BuildDiagnostics diagnostics, int shown)
    {
        if (shown is 0 || shown >= Parsed(diagnostics))
            return;

        response.Note(string.Create(CultureInfo.InvariantCulture, $"warnings={diagnostics.Warnings.Length} hidden"));
    }
    private static void AppendFailureOutput(ResponseBuilder response, ProcessRun run, int reported, bool verbose, string root)
    {
        if (reported > 0 || run.ExitCode is 0)
            return;

        var diagnostics = Diagnostics(run.Output);
        var shown = ErrorsUnlessVerbose(diagnostics, verbose);

        AppendHiddenWarnings(response, diagnostics, shown.Length);

        foreach (var line in shown)
            response.Line(Relative(line, root));

        AppendTail(response, run, diagnostics.Errors.Length, root);
    }
    private static int Parsed(BuildDiagnostics diagnostics) => diagnostics.Errors.Length + diagnostics.Warnings.Length;

    private static string[] ErrorsUnlessVerbose(BuildDiagnostics diagnostics, bool verbose) =>
        verbose ? [.. diagnostics.Errors, .. diagnostics.Warnings] : diagnostics.Errors;

    internal static async Task<DotnetInstallation> InstalledAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var selected = await RunAsync(["--version"], workingDirectory, ProbeTimeout, cancellationToken).ConfigureAwait(false);
        var sdks = await RunAsync(["--list-sdks"], workingDirectory, ProbeTimeout, cancellationToken).ConfigureAwait(false);
        var runtimes = await RunAsync(["--list-runtimes"], workingDirectory, ProbeTimeout, cancellationToken).ConfigureAwait(false);

        return new DotnetInstallation(
            selected.ExitCode is 0 ? selected.Output.Trim() : string.Empty,
            sdks.ExitCode is 0 ? Leading(sdks.Output, ' ') : [],
            runtimes.ExitCode is 0 ? Leading(runtimes.Output, '[') : []);
    }

    private static string[] Leading(string output, char terminator)
    {
        var values = new List<string>(16);

        foreach (var line in output.AsSpan().EnumerateLines())
        {
            var end = line.IndexOf(terminator);

            if (end > 0)
                values.Add(new string(line[..end].TrimEnd()));
        }

        return [.. values];
    }

    internal static string PerProject(TestRunReport report) => report.Projects.Length < 2
    ? string.Empty
    : "  " + string.Join("  ", report.Projects.Select(project => string.Create(
        CultureInfo.InvariantCulture,
        $"{project.Project}:{project.Total}/{project.DurationMs}ms")));

    internal const string ResultsDirectoryVariable = "TERSE_RESULTS_DIRECTORY";
    private const int MaxNoteLines = 20;

    private static async Task<string[]> NotesAsync(DirectoryInfo results, CancellationToken cancellationToken)
    {
        var notes = new List<string>(MaxNoteLines);

        foreach (var file in results.EnumerateFiles("terse-notes*.txt", RecursiveAndTolerant))
        {
            if (notes.Count >= MaxNoteLines)
                break;

            if (file.Length > MaxNoteBytes)
                continue;

            notes.AddRange(await ReadNoteAsync(file, cancellationToken).ConfigureAwait(false));
        }

        return [.. notes.Take(MaxNoteLines)];
    }

    private static async Task<IEnumerable<string>> ReadNoteAsync(FileInfo file, CancellationToken cancellationToken)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync(file.FullName, cancellationToken).ConfigureAwait(false);

            return lines.Where(line => line.Length > 0);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string Noted(string response, string[] notes) => notes.Length is 0
        ? response
        : response + "\nrun notes:\n" + string.Join('\n', notes);

    private const long MaxNoteBytes = 64 * 1024;
    private static readonly EnumerationOptions RecursiveAndTolerant = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
    };
    private const string ProbeAssembly = "terse.dll";

    [GeneratedRegex(@"(?m)^\s*\S+ -> (?<path>.+\.(?:dll|exe))\s*$")]
    private static partial Regex WrittenOutput();


    private static string Probe(string assembly, string target) => string.Create(
        CultureInfo.InvariantCulture,
        $"probe: dotnet \"{assembly}\" call <tool> --workspace \"{target}\" --json '{{...}}'  - answers from the binary this build just wrote, not from the connected server");

    internal static string[] OutputNotes(string output, string root, string target)
    {
        var notes = new List<string>();

        foreach (Match match in WrittenOutput().Matches(output))
        {
            var path = match.Groups["path"].Value;

            notes.Add("wrote " + PositionFormat.Relative(root, path));

            if (Path.GetFileName(path.AsSpan()).Equals(ProbeAssembly, StringComparison.OrdinalIgnoreCase))
                notes.Add(Probe(path, target));
        }

        return [.. notes];
    }

    private static void AppendOutputs(ResponseBuilder response, ProcessRun run, string root, string target)
    {
        foreach (var note in OutputNotes(run.Output, root, target))
            response.Note(note);
    }
}

internal sealed record ProcessRun(
    int ExitCode,
    string Output,
    long ElapsedMilliseconds,
    bool TimedOut = false,
    string StandardOutput = "",
    string StandardError = "");

internal readonly record struct TestRunResult(string Response, TestRunReport Report, bool Locked);

public readonly record struct BuildRun(string Response, bool Locked);

internal readonly record struct BuildDiagnostics(string[] Errors, string[] Warnings);

internal readonly record struct DotnetInstallation(string Selected, string[] Sdks, string[] Runtimes);
