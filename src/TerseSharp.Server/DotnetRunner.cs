using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
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
        var run = await BuiltAsync(workspace, target, scope, DefaultTimeout, cancellationToken).ConfigureAwait(false);

        return new BuildRun(RenderBuild(target, workspace.Root, run, verbose), Locked(run));
    }
    private static bool Locked(ProcessRun run) => !run.Stopped && IsLockedOutput(run.ExitCode, run.Output);

    private static bool IsGreen(ProcessRun run, TestRunReport report) =>
        run.ExitCode is 0 && !run.TimedOut && run.Drained && report.Total > 0 && report.Failures.Length is 0;

    private static string QuietTest(TestRunReport report, ProcessRun run) => string.Create(
    CultureInfo.InvariantCulture,
    $"run_tests PASSED  passed={report.Passed} skipped={report.Skipped} total={report.Total} durationMs={report.DurationMs} elapsedMs={run.ElapsedMilliseconds}{Concurrency(report, run)}") + PerProject(report);

    internal static bool IsLockedOutput(int exitCode, string output) =>
        exitCode is not 0 && LockedOutput().IsMatch(output);

    internal static async Task<TestRunResult> TestAsync(
        WorkspaceTarget workspace,
        TestRunRequest request,
        CancellationToken cancellationToken)
    {
        var reported = request with
        {
            Reporter = await TestReporterProbe.DetectAsync(workspace.Root, request.Target, cancellationToken).ConfigureAwait(false),
        };

        if (Refused(reported) is { } refusal)
            return new TestRunResult(refusal.Render(), TestRunReport.Empty, false);

        var expanded = Expanded(workspace, reported);
        var results = Directory.CreateTempSubdirectory("terse-tests-");

        try
        {
            var prepared = await PreparedAsync(workspace, expanded, cancellationToken).ConfigureAwait(false);

            return prepared.Failure is { } failed
                ? new TestRunResult(failed.Response, Report(results, workspace.Root), failed.Locked)
                : await RanAsync(workspace, expanded, results, prepared, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Discard(results);
        }
    }

    private static string Interrupted(string response, List<string> missing, int invocations, bool serial, bool timedOut) => missing.Count is 0
        ? response
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{response}\nWARNING {Stopped(missing, invocations, serial, timedOut)}: {string.Join(", ", missing)}");

    internal static string Stopped(List<string> missing, int invocations, bool serial, bool timedOut)
    {
        var cause = timedOut ? "timed out" : "produced no results";

        return (invocations, serial, missing.Count == invocations) switch
        {
            (1, _, _) => timedOut ? "this run timed out and produced no results" : "this run produced no results",
            (_, true, _) => string.Create(CultureInfo.InvariantCulture, $"the batch stopped at the first project that {cause}; {missing.Count} of {invocations} project(s) produced no results"),
            (_, _, true) => string.Create(CultureInfo.InvariantCulture, $"every project of the batch {cause}; all {invocations} produced no results"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{missing.Count} of {invocations} project(s) {cause}; the rest of the batch still ran"),
        };
    }

    private static Task<(ProcessRun Run, List<string> Missing)> InvokeAsync(
        WorkspaceTarget workspace,
        TestRunRequest request,
        string resultsDirectory,
        long preparedMilliseconds,
        CancellationToken cancellationToken) => request.IsSerial
        ? SequentialAsync(workspace, request, resultsDirectory, cancellationToken)
        : ConcurrentAsync(workspace, request with { NoBuild = true }, resultsDirectory, preparedMilliseconds, cancellationToken);

    internal static ProcessRun Merge(ProcessRun first, ProcessRun next) => new(
        first.ExitCode is 0 ? next.ExitCode : first.ExitCode,
        first.Output + "\n" + next.Output,
        first.ElapsedMilliseconds + next.ElapsedMilliseconds,
        first.TimedOut || next.TimedOut,
        first.StandardOutput + "\n" + next.StandardOutput,
        first.StandardError + "\n" + next.StandardError,
        first.Drained && next.Drained,
        first.Stopped || next.Stopped,
        Joined(first.Command, next.Command));

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

    internal static string[] Arguments(TestRunRequest request, string resultsDirectory) => request.Target switch
    {
        var target when request.Direct && IsAssembly(target) => AssemblyArguments(request, resultsDirectory),
        _ when request.Reporter is TestReporter.VsTestLogger => VsTestArguments(request, resultsDirectory),
        _ => TestPlatformArguments.Of(request, resultsDirectory),
    };

    private static string[] VsTestArguments(TestRunRequest request, string resultsDirectory)
    {
        var arguments = new List<string>(16)
        {
            "test", request.Target, "-nodeReuse:false", "--nologo", "--logger", "trx", "--results-directory", resultsDirectory,
        };

        if (HangWindow(request.Timeout) is { } window)
        {
            arguments.AddRange([
                "--blame-hang-timeout",
                string.Create(CultureInfo.InvariantCulture, $"{(long)window.TotalMilliseconds}ms"),
                "--blame-hang-dump-type",
                "none",
            ]);
        }

        if (request.Filter is { Length: > 0 } filter)
            arguments.AddRange(["--filter", filter]);

        if (request.NoBuild)
            arguments.Add("--no-build");

        var scoped = request.Scope.Applied(arguments);

        return request.RunSettings.IsDefaultOrEmpty ? scoped : [.. scoped, "--", .. request.RunSettings];
    }

    internal static string RenderBuild(string target, string root, ProcessRun run, bool verbose)
    {
        var diagnostics = Diagnostics(run.Output);

        if (!verbose && run.ExitCode is 0 && run.Drained && diagnostics.Errors.Length is 0)
            return QuietBuild(run, diagnostics.Warnings.Length);

        var shown = Shown(diagnostics, verbose);
        var response = new ResponseBuilder("build", target).Verbose(verbose);

        response.Summary(shown.Length, Parsed(diagnostics), "diagnostics");
        response.Note(string.Create(CultureInfo.InvariantCulture, $"exitCode={run.ExitCode} elapsedMs={run.ElapsedMilliseconds}"));

        if (verbose || run.ExitCode is not 0 || !run.Drained)
            AppendCommand(response, run);

        if (verbose)
            AppendOutputs(response, run, root, target);

        AppendLockWarning(response, run);
        AppendDrainWarning(response, run);
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

    internal static string RenderTest(ProcessRun run, TestRunReport report, TestRunRequest request, string root)
    {
        if (report.Total is 0 && run.ExitCode is not 0)
            return RenderNoResults(request.Target, run, request.Verbose, root, request.Timeout);

        if (IsGreen(run, report) && !request.WantsDetail)
            return QuietTest(report, run);

        var shown = Math.Min(report.Failures.Length, MaxFailures);
        var response = new ResponseBuilder("run_tests", request.Target).Verbose(request.Verbose);

        response.Summary(shown, report.Failures.Length, "failures");
        response.Note(Counters(report, run));

        if (request.Verbose)
            AppendCommand(response, run);

        AppendWarnings(response, run, report, request.Filter);
        AppendFailures(response, report, shown);
        AppendTimings(response, report, request);

        return response.ToString();
    }
    internal static string RenderNoResults(string target, ProcessRun run, bool verbose, string root = "", TimeSpan deadline = default)
    {
        var response = new ResponseBuilder("run_tests", target).Verbose(verbose);

        response.Note(run.TimedOut
            ? string.Create(CultureInfo.InvariantCulture, $"FAILED timed out after {run.ElapsedMilliseconds} ms, no test results were produced")
            : string.Create(CultureInfo.InvariantCulture, $"FAILED exitCode={run.ExitCode} elapsedMs={run.ElapsedMilliseconds}, no test results were produced"));

        AppendCommand(response, run);
        AppendLockWarning(response, run);
        AppendDrainWarning(response, run);
        AppendDeadlineRemedy(response, run, deadline);
        AppendFailureOutput(response, run, reported: 0, verbose, root);

        return response.ToString();
    }
    internal static string RenderTestNames(string target, ProcessRun run, string? contains, string root = "") =>
        RenderTestNames(target, run, TestNameList.Parse(run.Output, contains), root);

    private static string Counters(TestRunReport report, ProcessRun run) => string.Create(
        CultureInfo.InvariantCulture,
        $"passed={report.Passed} failed={report.Failed} skipped={report.Skipped} total={report.Total} durationMs={report.DurationMs} exitCode={run.ExitCode} elapsedMs={run.ElapsedMilliseconds}{Concurrency(report, run)}{Slowest(report, run)}") + PerProject(report);

    private static void AppendWarnings(ResponseBuilder response, ProcessRun run, TestRunReport report, string? filter)
    {
        AppendLockWarning(response, run);
        AppendDrainWarning(response, run);

        if (run.TimedOut)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"WARNING timed out after {run.ElapsedMilliseconds} ms; the results below are partial"));

        AppendDeadlineRemedy(response, run);

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
        var reporter = await TestReporterProbe.DetectAsync(workspace.Root, target, cancellationToken).ConfigureAwait(false);

        if (reporter is not TestReporter.VsTestLogger)
            return await ListedFromModulesAsync(workspace, target, contains, scope, timeout, cancellationToken).ConfigureAwait(false);

        var arguments = scope.Applied(["test", target, "-nodeReuse:false", "--nologo", "--list-tests"]);
        var run = await RunAsync(arguments, workspace.Root, timeout, cancellationToken).ConfigureAwait(false);

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
            Answered(selected) ? selected.Output.Trim() : string.Empty,
            Answered(sdks) ? Leading(sdks.Output, ' ') : [],
            Answered(runtimes) ? Leading(runtimes.Output, '[') : []);
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

    internal static async Task<string> AuditPackagesAsync(
        string root,
        string projectPath,
        bool vulnerable,
        CancellationToken cancellationToken)
    {
        var flag = vulnerable ? "--vulnerable" : "--outdated";
        var run = await RunAsync(["list", projectPath, "package", flag, "--include-transitive"], root, TimeSpan.FromMinutes(3), cancellationToken).ConfigureAwait(false);
        var response = new ResponseBuilder("package_list", PositionFormat.Relative(root, projectPath));

        if (run.ExitCode is not 0 || !run.Drained)
        {
            response.Summary(0, 0, "packages examined");
            response.Note(Unexamined(run, flag));

            foreach (var line in Tail(run.Output))
                response.Line(line);

            return response.ToString();
        }

        var lines = Audited(run.Output);

        response.Summary(lines.Length, lines.Length, vulnerable ? "vulnerable packages" : "outdated packages");

        foreach (var line in lines)
            response.Line(line);

        return response.ToString();
    }

    private static string[] Audited(string output)
    {
        var kept = new List<string>();

        foreach (var line in output.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
                kept.Add(new string(trimmed[2..]));
        }

        return [.. kept];
    }

    private static async Task<ProcessRun> BuiltAsync(
        WorkspaceTarget workspace,
        string target,
        BuildScope scope,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var restore = RestoreIsStale(workspace);
        var run = await RunAsync(BuildArguments(target, scope, restore), workspace.Root, timeout, cancellationToken).ConfigureAwait(false);

        if (FailedForMissingAssets(run))
        {
            var retried = await RunAsync(BuildArguments(target, scope, restore: true), workspace.Root, timeout, cancellationToken).ConfigureAwait(false);

            restore = true;
            run = retried with { ElapsedMilliseconds = run.ElapsedMilliseconds + retried.ElapsedMilliseconds };
        }

        if (restore && run.ExitCode is 0)
            Restored[workspace.Root] = RestoreInputStamp(workspace);

        return run;
    }

    private static string Slot(string resultsDirectory, int index, int invocations)
    {
        var slot = SlotPath(resultsDirectory, index, invocations);

        Directory.CreateDirectory(slot);

        return slot;
    }

    private static KeyValuePair<string, string>[] ResultsEnvironment(string resultsDirectory) =>
        [new(ResultsDirectoryVariable, resultsDirectory)];

    private static ProcessRun Batched(ProcessRun?[] runs, long elapsedMilliseconds)
    {
        var merged = default(ProcessRun);

        foreach (var run in runs)
        {
            if (run is not null)
                merged = merged is null ? run : Merge(merged, run);
        }

        return merged is null
            ? new ProcessRun(0, string.Empty, 0)
            : merged with { ElapsedMilliseconds = elapsedMilliseconds };
    }

    private static long Elapsed(ProcessRun?[] runs)
    {
        var elapsed = 0L;

        foreach (var run in runs)
        {
            if (run is not null)
                elapsed += run.ElapsedMilliseconds;
        }

        return elapsed;
    }

    internal static List<string> Unfinished(ImmutableArray<string> targets, ProcessRun?[] runs, string resultsDirectory)
    {
        var missing = new List<string>(targets.Length);

        for (var index = 0; index < runs.Length; index++)
        {
            if (runs[index] is null or { TimedOut: true } || !Finished(SlotPath(resultsDirectory, index, targets.Length)))
                missing.Add(Path.GetFileNameWithoutExtension(targets[index]));
        }

        return missing;
    }

    private static Task<ProcessRun> SlottedAsync(
        WorkspaceTarget workspace,
        TestRunRequest request,
        string resultsDirectory,
        int index,
        CancellationToken cancellationToken)
    {
        var targets = request.Invocations;
        var slot = Slot(resultsDirectory, index, targets.Length);

        return RunAsync(
            Arguments(request with { Target = targets[index] }, slot),
            workspace.Root,
            request.Timeout,
            cancellationToken,
            ResultsEnvironment(slot));
    }

    private static async Task<(ProcessRun Run, List<string> Missing)> SequentialAsync(
        WorkspaceTarget workspace,
        TestRunRequest request,
        string resultsDirectory,
        CancellationToken cancellationToken)
    {
        var targets = request.Invocations;
        var runs = new ProcessRun?[targets.Length];

        for (var index = 0; index < targets.Length; index++)
        {
            runs[index] = await SlottedAsync(workspace, request, resultsDirectory, index, cancellationToken).ConfigureAwait(false);

            if (runs[index] is { TimedOut: true } || !Finished(SlotPath(resultsDirectory, index, targets.Length)))
                break;
        }

        return (Batched(runs, Elapsed(runs)), Unfinished(targets, runs, resultsDirectory));
    }

    private static async Task<(ProcessRun Run, List<string> Missing)> ConcurrentAsync(
        WorkspaceTarget workspace,
        TestRunRequest request,
        string resultsDirectory,
        long preparedMilliseconds,
        CancellationToken cancellationToken)
    {
        var targets = request.Invocations;
        var runs = new ProcessRun?[targets.Length];
        var options = new ParallelOptions { MaxDegreeOfParallelism = request.Degree, CancellationToken = cancellationToken };
        var stopwatch = Stopwatch.StartNew();

        await Parallel.ForAsync(0, targets.Length, options, async (index, token) =>
            runs[index] = await SlottedAsync(workspace, request, resultsDirectory, index, token).ConfigureAwait(false)).ConfigureAwait(false);

        return (Batched(runs, preparedMilliseconds + stopwatch.ElapsedMilliseconds), Unfinished(targets, runs, resultsDirectory));
    }

    private static async Task<PreparedBuild> PreparedAsync(
        WorkspaceTarget workspace,
        TestRunRequest request,
        CancellationToken cancellationToken)
    {
        if (request.IsSerial || request.NoBuild)
            return new PreparedBuild(0, null);

        var elapsed = 0L;
        var warnings = 0;

        foreach (var target in request.Builds)
        {
            var run = await BuiltAsync(workspace, target, request.Scope, request.Timeout, cancellationToken).ConfigureAwait(false);
            var diagnostics = Diagnostics(run.Output);

            elapsed += run.ElapsedMilliseconds;
            warnings += diagnostics.Warnings.Length;

            if (run.ExitCode is not 0 || diagnostics.Errors.Length is not 0)
                return new PreparedBuild(elapsed, BatchBuildFailure(workspace, request, target, run));
        }

        return new PreparedBuild(elapsed, null, BuildVerdict(warnings));
    }

    private static string Outcome(ProcessRun run) => run.TimedOut ? "timed out" : "failed";

    private static BuildRun BatchBuildFailure(WorkspaceTarget workspace, TestRunRequest request, string target, ProcessRun run) => new(
        string.Create(
            CultureInfo.InvariantCulture,
            $"{RenderBuild(target, workspace.Root, run, request.Verbose)}\nWARNING the batch build of {Path.GetFileNameWithoutExtension(target)} {Outcome(run)}, so no project ran; {Recovery(run)}"),
        Locked(run));

    private static string Recovery(ProcessRun run) => run.TimedOut
        ? "raise timeoutSeconds, or pass parallel=1 to build each project inside its own run"
        : "fix the errors above, or pass parallel=1 to build each project inside its own run";

    private static async Task<TestRunResult> RanAsync(
        WorkspaceTarget workspace,
        TestRunRequest request,
        DirectoryInfo results,
        PreparedBuild prepared,
        CancellationToken cancellationToken)
    {
        var (run, missing) = await InvokeAsync(workspace, request, results.FullName, prepared.ElapsedMilliseconds, cancellationToken).ConfigureAwait(false);
        var report = Report(results, workspace.Root);
        var notes = request.Verbose ? await NotesAsync(results, cancellationToken).ConfigureAwait(false) : [];
        var hung = await HangSequence.ActiveAsync(results, cancellationToken).ConfigureAwait(false);
        var response = Noted(RenderTest(run, report, request, workspace.Root), notes);
        var stopped = run.TimedOut || hung.Length is not 0;

        return new TestRunResult(
            Verdicted(prepared.Verdict ?? Built(request, run, report), Hung(Interrupted(response, missing, request.Invocations.Length, request.IsSerial, stopped), hung)),
            report,
            Locked(run));
    }

    internal static TimeSpan? HangWindow(TimeSpan timeout) =>
        timeout > HangMargin + HangMargin ? timeout - HangMargin : null;

    internal static string Hung(string response, string[] hung) => hung.Length is 0
        ? response
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{response}\nWARNING the run was stopped while these test(s) were still running: {string.Join(", ", hung)}");

    private static readonly TimeSpan HangMargin = TimeSpan.FromSeconds(15);

    private static bool Produced(string slot) =>
        Directory.Exists(slot) && Directory.EnumerateFiles(slot, "*.trx", SearchOption.AllDirectories).Any();

    private static string SlotPath(string resultsDirectory, int index, int invocations) =>
        invocations is 1 ? resultsDirectory : Path.Combine(resultsDirectory, index.ToString(CultureInfo.InvariantCulture));

    internal static bool Finished(string slot) => Produced(slot) && !Aborted(slot);

    private static bool Aborted(string slot) =>
        Directory.Exists(slot) && Directory.EnumerateFiles(slot, "*Sequence*.xml", SearchOption.AllDirectories).Any();

    private static void AppendDeadlineRemedy(ResponseBuilder response, ProcessRun run, TimeSpan deadline = default)
    {
        if (!run.TimedOut && !Deadlined(run, deadline))
            return;

        response.Note("remedy: the run reached its deadline before it finished; raise timeoutSeconds, or narrow the run with test= or filter=");
    }

    private static void AppendDrainWarning(ResponseBuilder response, ProcessRun run)
    {
        if (run.Drained)
            return;

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"WARNING the process exited but its output stream stayed open, so what was captured is incomplete - captured={run.Output.Length} chars"));
    }

    private static bool Answered(ProcessRun run) => run.ExitCode is 0 && run.Drained;

    private static string Unexamined(ProcessRun run, string flag) => run.Drained
        ? string.Create(CultureInfo.InvariantCulture, $"FAILED dotnet list package {flag} exited {run.ExitCode}; nothing was examined, so this is not a clean bill of health")
        : string.Create(CultureInfo.InvariantCulture, $"FAILED dotnet list package {flag} exited but its output stream stayed open, so the listing is incomplete; this is not a clean bill of health");

    private static void AppendStoppedWarning(ResponseBuilder response, ProcessRun run)
    {
        if (!run.Stopped)
            return;

        response.Note(string.Create(CultureInfo.InvariantCulture, $"WARNING the process tree was killed after {run.ElapsedMilliseconds} ms, so this listing is partial and is not the whole suite"));
        response.Note("remedy: raise timeoutSeconds, or list one project at a time with project=");
    }

    private static TerseError? Refused(TestRunRequest request) => request.Reporter switch
    {
        TestReporter.VsTestLogger => null,
        TestReporter.Unknown => Errors.UnsupportedRunner(
            "run_tests",
            "no project under this workspace declares a trx reporter, so the run would produce no results terse can read",
            "reference Microsoft.Testing.Extensions.TrxReport from the test project, or use xunit.v3, whose runner writes the report itself"),
        _ when !request.RunSettings.IsDefaultOrEmpty => Errors.UnsupportedRunner(
            "run_tests runSettings=",
            "those are VSTest RunSettings overrides, and forwarding them would make the test application refuse the whole session",
            "bound parallelism with the test framework's own configuration file - xunit.v3 reads xunit.runner.json - and re-run without runSettings="),
        _ when TestPlatformArguments.Untranslatable(request.Reporter, request.Filter) => Errors.UnsupportedRunner(
            "run_tests filter=",
            "xunit.v3 accepts the VSTest filter syntax only from 4.0.0, so this expression cannot be selected on every supported version",
            "pass test=\"Namespace.Class\" or test=\"Namespace.Class.Method\" instead, which is translated to the --filter-method every xunit.v3 accepts"),
        _ => null,
    };

    internal static string RenderTestNames(string target, ProcessRun run, string[] names, string root)
    {
        var shown = Math.Min(names.Length, MaxTestNames);
        var response = new ResponseBuilder("list_tests", target);

        response.Summary(shown, names.Length, "tests");

        AppendLockWarning(response, run);
        AppendDrainWarning(response, run);
        AppendStoppedWarning(response, run);

        for (var index = 0; index < shown; index++)
            response.Line(names[index]);

        AppendFailureOutput(response, run, names.Length, verbose: false, root);

        return response.ToString();
    }

    private static async Task<BuildRun> ListedFromModulesAsync(
            WorkspaceTarget workspace,
            string target,
            string? contains,
            BuildScope scope,
            TimeSpan timeout,
            CancellationToken cancellationToken)
    {
        var build = await BuiltAsync(workspace, target, scope, timeout, cancellationToken).ConfigureAwait(false);

        if (build.ExitCode is not 0 || Diagnostics(build.Output).Errors.Length is not 0)
            return new BuildRun(RenderBuild(target, workspace.Root, build, verbose: false), Locked(build));

        var projects = await TestReporterProbe.TestProjectsAsync(workspace.Root, target, cancellationToken).ConfigureAwait(false);
        var modules = await ModulesAsync(workspace, projects, scope, timeout, cancellationToken).ConfigureAwait(false);

        return modules.Length is 0
            ? new BuildRun(NoTestModule(target, projects).Render(), Locked(build))
            : await ListedFromAsync(workspace, target, contains, modules, timeout, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<BuildRun> ListedFromAsync(
        WorkspaceTarget workspace,
        string target,
        string? contains,
        string[] modules,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var merged = await ListedAsync(workspace, modules[0], timeout, cancellationToken).ConfigureAwait(false);
        var outputs = new List<string>(modules.Length) { merged.Output };

        for (var index = 1; index < modules.Length; index++)
        {
            var run = await ListedAsync(workspace, modules[index], timeout, cancellationToken).ConfigureAwait(false);

            outputs.Add(run.Output);
            merged = Merge(merged, run);
        }

        return new BuildRun(RenderTestNames(target, merged, TestNameList.Parse(outputs, contains), workspace.Root), Locked(merged));
    }

    private static Task<ProcessRun> ListedAsync(WorkspaceTarget workspace, string module, TimeSpan timeout, CancellationToken cancellationToken) =>
        RunAsync([module, "--list-tests"], workspace.Root, timeout, cancellationToken);

    private static TerseError NoTestModule(string target, ImmutableArray<string> scanned) => Errors.Invalid(
        string.Create(
            CultureInfo.InvariantCulture,
            $"the build of {Path.GetFileName(target)} wrote no test module under the workspace root, so there is nothing to list; list_tests selects test projects by scanning each .csproj for a test framework, and that scan matched {scanned.Length} project(s){Scanned(scanned)}"),
        "pass project= naming a test project; a multi-targeted test project needs targetFramework= too, because its TargetPath does not resolve without one. run_tests selects by metadata reference instead, so the two can disagree on a solution whose test framework arrives through Directory.Build.props");

    private static async Task<string[]> ModulesAsync(
        WorkspaceTarget workspace,
        ImmutableArray<string> projects,
        BuildScope scope,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var modules = new List<string>(projects.Length);

        foreach (var project in projects)
        {
            var arguments = scope.AsProperties(["msbuild", project, "-getProperty:TargetPath", "-nologo"]);
            var run = await RunAsync(arguments, workspace.Root, timeout, cancellationToken).ConfigureAwait(false);
            var path = LastLine(run.StandardOutput);

            if (run.ExitCode is 0 && IsTestModule(workspace.Root, path))
                modules.Add(path);
        }

        return [.. modules.Distinct(PathBoundary.Comparer)];
    }

    private static bool IsTestModule(string root, string path) =>
        path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        && PathBoundary.Contains(root, path)
        && File.Exists(path);

    private static string LastLine(string output)
    {
        var last = ReadOnlySpan<char>.Empty;

        foreach (var line in output.AsSpan().EnumerateLines())
        {
            if (!line.IsWhiteSpace())
                last = line.Trim();
        }

        return last.ToString();
    }

    private static void AppendCommand(ResponseBuilder response, ProcessRun run)
    {
        if (run.Command is { Length: > 0 } command)
            response.Note("command: " + command);
    }

    private static bool Deadlined(ProcessRun run, TimeSpan deadline) =>
        HangWindow(deadline) is { } window && run.ElapsedMilliseconds >= (long)window.TotalMilliseconds;

    private static string Concurrency(TestRunReport report, ProcessRun run) =>
            report.Projects.Length < 2 || run.ElapsedMilliseconds <= 0 || report.DurationMs <= 0
                ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $" concurrency={(double)report.DurationMs / run.ElapsedMilliseconds:F1}x");

    private static string Slowest(TestRunReport report, ProcessRun run)
    {
        if (report.Projects.Length < 2 || report.PassedTests.IsDefaultOrEmpty || run.ElapsedMilliseconds <= 0 || report.DurationMs <= 0)
            return string.Empty;

        if ((double)report.DurationMs / run.ElapsedMilliseconds >= SerialConcurrency)
            return string.Empty;

        var widest = report.PassedTests.MaxBy(test => test.DurationMs);

        return string.Create(CultureInfo.InvariantCulture, $" slowestTest={widest.Name} {widest.DurationMs}ms");
    }

    private const double SerialConcurrency = 2;
    private const int MinimumExpandedProjects = 2;

    private static TestRunRequest Expanded(WorkspaceTarget workspace, TestRunRequest request) =>
        Expandable(request, workspace.TestProjects)
            ? request with { Targets = workspace.TestProjects, BuildTarget = request.Target, Direct = true }
            : request;

    internal static bool Expandable(TestRunRequest request, ImmutableArray<string> testProjects) =>
        !testProjects.IsDefaultOrEmpty
        && testProjects.Length >= MinimumExpandedProjects
        && request.Targets.IsDefaultOrEmpty
        && request.Parallel is not 1
        && request.Filter is not { Length: > 0 }
        && request.RunSettings.IsDefaultOrEmpty
        && request.Scope.IsDefault
        && request.Reporter is TestReporter.VsTestLogger
        && SolutionFile.IsSolutionFile(request.Target);

    private static string Joined(string first, string next) => (first.Length, next.Length) switch
    {
        (0, _) => next,
        (_, 0) => first,
        _ => first + " && " + next,
    };

    internal static bool IsAssembly(string target) => target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    private static string[] AssemblyArguments(TestRunRequest request, string resultsDirectory) =>
    [
        request.Target,
    "-trx",
    Path.Combine(resultsDirectory, "results.trx"),
    "-noLogo",
    "-reporter",
    "quiet",
];

    internal static string[] BuildArguments(string target, BuildScope scope, bool restore)
    {
        string[] arguments = restore
            ? ["build", target, "-nodeReuse:false", "-v", "q", "--nologo"]
            : ["build", target, "-nodeReuse:false", "-v", "q", "--nologo", "--no-restore"];

        return scope.Applied(arguments);
    }

    internal static bool FailedForMissingAssets(ProcessRun run)
    {
        if (run.ExitCode is 0 || run.TimedOut || run.Stopped)
            return false;

        foreach (var marker in RestoreFailures)
        {
            if (run.Output.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static bool RestoreIsStale(WorkspaceTarget workspace) =>
        workspace.ProjectPaths.IsDefaultOrEmpty
        || !Restorable(workspace)
        || !Restored.TryGetValue(workspace.Root, out var stamp)
        || stamp != RestoreInputStamp(workspace);

    internal static int RestoreInputStamp(WorkspaceTarget workspace)
    {
        var stamp = default(HashCode);
        var walked = new HashSet<string>(StringComparer.FromComparison(PathBoundary.Comparison));

        foreach (var project in workspace.ProjectPaths)
        {
            stamp.Add(File.GetLastWriteTimeUtc(project));

            for (var current = Path.GetDirectoryName(project); current is { Length: > 0 }; current = Path.GetDirectoryName(current))
            {
                if (walked.Add(current))
                    Stamp(ref stamp, current);
            }
        }

        return stamp.ToHashCode();
    }

    private static readonly string[] RestoreInputs =
    [
        "Directory.Packages.props",
        "Directory.Build.props",
        "Directory.Build.targets",
        "packages.lock.json",
        "global.json",
        "nuget.config",
        "NuGet.Config",
    ];

    private static string[] RestoreFailures =>
    [
        "NETSDK1004",
        "NETSDK1064",
        "NU1100",
        "NU1101",
        "NU1102",
        "project.assets.json",
    ];

    private static void Stamp(ref HashCode stamp, string directory)
    {
        foreach (var name in RestoreInputs)
            stamp.Add(File.GetLastWriteTimeUtc(Path.Combine(directory, name)));
    }

    private static readonly ConcurrentDictionary<string, int> Restored = new(StringComparer.FromComparison(PathBoundary.Comparison));

    private static bool Restorable(WorkspaceTarget workspace)
    {
        foreach (var project in workspace.ProjectPaths)
        {
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(project) ?? workspace.Root, "obj", "project.assets.json")))
                return false;
        }

        return true;
    }

    private static string Scanned(ImmutableArray<string> scanned) => scanned.Length is 0
        ? string.Empty
        : ": " + string.Join(", ", scanned.Take(5).Select(project => Path.GetFileName(project.AsSpan()).ToString()));

    private static string BuildVerdict(int warnings) => string.Create(
        CultureInfo.InvariantCulture,
        $"  build=ok errors=0 warnings={warnings}");

    private static string Verdicted(string? verdict, string response)
    {
        if (verdict is null)
            return response;

        var end = response.IndexOf('\n', StringComparison.Ordinal);

        return end < 0 ? response + verdict : response[..end] + verdict + response[end..];
    }

    private static string? Built(TestRunRequest request, ProcessRun run, TestRunReport report)
    {
        if (request.NoBuild || report.Total is 0)
            return null;

        var diagnostics = Diagnostics(run.Output);

        return diagnostics.Errors.Length is 0 ? BuildVerdict(diagnostics.Warnings.Length) : null;
    }
}

internal sealed record ProcessRun(
    int ExitCode,
    string Output,
    long ElapsedMilliseconds,
    bool TimedOut = false,
    string StandardOutput = "",
    string StandardError = "",
    bool Drained = true,
    bool Stopped = false,
    string Command = "");

internal readonly record struct TestRunResult(string Response, TestRunReport Report, bool Locked);

public readonly record struct BuildRun(string Response, bool Locked);

internal readonly record struct BuildDiagnostics(string[] Errors, string[] Warnings);

internal readonly record struct DotnetInstallation(string Selected, string[] Sdks, string[] Runtimes);

internal readonly record struct PreparedBuild(long ElapsedMilliseconds, BuildRun? Failure, string? Verdict = null);
