using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class BuildTools(ToolContext context, LastTestRun lastRun, UnchangedRun unchanged)
{
    private static readonly SearchValues<char> FilterSpecial = SearchValues.Create("\\()&|=!~");

    [McpServerTool(Name = "build")]
    [Description("Replaces Bash dotnet build. A successful build answers in one line - warnings are counted, never listed - and a failed build lists error-severity diagnostics only. Raw MSBuild output is never returned. Pass verbose=true for every diagnostic of every severity; configuration, targetFramework and properties scope the build.")]
    public Task<string> Build(
        [Description("Project path; empty builds the solution.")] string? project = null,
        [Description("Build configuration, passed to dotnet as -c, e.g. Release. Empty uses the SDK default, which is Debug.")] string? configuration = null,
        [Description("Target framework, passed to dotnet as -f, e.g. net10.0. Empty builds every framework a multi-targeted project declares.")] string? targetFramework = null,
        [Description("MSBuild properties, each written Name=Value and passed to dotnet as -p:Name=Value, e.g. [\"NativeAppHostEnabled=false\"]. Applied after configuration and targetFramework.")] string[]? properties = null,
        [Description("Return every diagnostic, warnings included, and the full report even when the build succeeds. Default false, which answers a successful build in one line and hides warnings on a failed one. The warnings= count reports what this build emitted, so a build that recompiled nothing reports 0.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithTargetAsync(workspace, project, target =>
        {
            if (SelfBuilt(target, Whole(project, configuration)) is { } refused)
                return Task.FromResult(refused);

            var scope = Scoped(configuration, targetFramework, properties);

            return scope.IsOk
                ? Contained(target, project, resolved => BuildWithRecoveryAsync(
                    target, resolved, scope.Value, verbose, cancellationToken))
                : Task.FromResult(scope.Error!.Render());
        },
        cancellationToken: cancellationToken);

    [McpServerTool(Name = "clean", Destructive = true)]
    [Description("Replaces Bash dotnet clean. Deletes the bin and obj directories of the workspace or of one project and reports how many files and bytes were freed, never raw MSBuild output. Unlike dotnet clean it also removes obj, and when the loaded workspace's own MSBuild file locks block the delete it unloads, retries and reloads. path= cleans a solution or project that is NOT loaded - a fixture, a sibling repository - so reproducing a cold build needs no load and no shell. A clean with nothing locked reports counters only; verbose=true adds the per-directory list. Not covered by undo_last_change.")]
    public Task<string> Clean(
    [Description("Project path; empty cleans every project under the workspace root.")] string? project = null,
    [Description("Also delete obj, the intermediate output. Default true; false leaves obj as dotnet clean does.")] bool includeIntermediate = true,
    [Description("List what would be deleted and delete nothing.")] bool dryRun = false,
    [Description("List every directory, not just the counters.")] bool verbose = false,
    [Description("Path to a .slnx, .sln, .slnf or project file that is not loaded. Its own directory is the root that is swept, so no workspace is loaded and nothing else is touched.")] string? path = null,
    [Description("Workspace or worktree name.")] string? workspace = null,
    CancellationToken cancellationToken = default)
    {
        var rejection = context.RejectWrite();

        if (rejection is not null)
            return Task.FromResult(rejection);

        return path is { Length: > 0 } unloaded
            ? Task.FromResult(CleanUnloaded(unloaded, includeIntermediate, dryRun, verbose, cancellationToken))
            : context.WithTargetAsync(
                workspace,
                project,
                target => SelfBuilt(target, !dryRun && project is not { Length: > 0 }) is { } refused
                    ? Task.FromResult(refused)
                    : CleanWithRecoveryAsync(target, project, includeIntermediate, dryRun, verbose, cancellationToken),
                cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "run_tests")]
    [Description("Replaces Bash dotnet test. A green run answers in one line - passed/skipped/total/durationMs; a test failure returns its message, expected and actual values, and one source frame, and a build that failed under the run returns error-severity diagnostics only. A repeat of a call that already answered GREEN with nothing written since is not re-run: it answers run_tests UNCHANGED naming the previous verdict and its age, and force=true re-runs it anyway. A stopped run names the test still running, on the dotnet test path. A SOLUTION, and a projects=[...] batch, run their test projects CONCURRENTLY. Replaces one call per project: one merged verdict line, a per-project timeout, each built up front then run with --no-build, and a project that times out is named without stopping the rest; a duplicate is refused. parallel bounds how many run at once and defaults to one per core; parallel=1 is serial, stops at the first timeout, and keeps a solution as ONE invocation. runSettings passes VSTest RunSettings overrides, which bound parallelism INSIDE one assembly. tests=[...] runs several tests, classes or namespaces in ONE call, combined into one filter expression. changed=true runs only the test projects your change can reach, naming what it ran and what it skipped. verbose=true gives the full report on a green run and the hidden warnings on a failed build; configuration, targetFramework and properties scope the run as they scope build.")]
    public Task<string> RunTests(
        [Description("Optional test to run: a fully-qualified test name, or a class or namespace prefix. Cannot be combined with filter.")] string? test = null,
        [Description("Optional VSTest filter expression. Cannot be combined with test. Microsoft.Testing.Platform takes FullyQualifiedName only; anything else is refused naming test=.")] string? filter = null,
        [Description("Project path; empty runs every test project.")] string? project = null,
        [Description("Several test projects in one call, at most 10, each a name or a path to its .csproj, run concurrently under parallel. Not with project=.")] string?[]? projects = null,
        [Description("Run only the test projects that transitively reference a project changed since the workspace loaded; falls back to the whole solution naming the reason. Ignored when project is passed. Default false.")] bool changed = false,
        [Description("How many projects of a batch run at once, 0-10. 0 is one per core, 1 is serial and stops at the first timeout.")] int parallel = 0,
        [Description("VSTest RunSettings overrides, each Name=Value, e.g. [\"xUnit.MaxParallelThreads=1\"] - parallelism inside one assembly, which parallel does not touch. VSTest only; refused under Microsoft.Testing.Platform.")] string[]? runSettings = null,
        [Description("Build configuration, passed to dotnet as -c, e.g. Release. Empty uses the SDK default, which is Debug.")] string? configuration = null,
        [Description("Target framework, passed to dotnet as -f, e.g. net10.0. Empty runs every framework a multi-targeted test project declares.")] string? targetFramework = null,
        [Description("MSBuild properties, each written Name=Value and passed to dotnet as -p:Name=Value, e.g. [\"NativeAppHostEnabled=false\"]. Applied after configuration and targetFramework.")] string[]? properties = null,
        [Description("Run existing binaries; skip the build, including a batch's per-project build.")] bool noBuild = false,
        [Description("List passing tests too.")] bool includePassed = false,
        [Description("List the N slowest tests.")] int slowest = 0,
        [Description("Return the full report even when every test passed, and the warnings of a build that failed under the run. Default false, which answers a green run in one line and reports errors only.")] bool verbose = false,
        [Description("Timeout seconds, 1-3600 (600). With projects= or a solution it is the budget for EACH project and its build; above 30s it is also a per-test ceiling 15s below it.")] int timeoutSeconds = 600,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Several tests, classes or namespace prefixes in ONE call, at most 10, combined into one filter. Replaces one call per class; a blank entry is refused by index, not with filter.")] string?[]? tests = null,
        [Description("Run even when this exact call already answered green and nothing has been written since. Default false.")] bool force = false,
        CancellationToken cancellationToken = default) => Replayable(
        Key(
            "run_tests",
            [test, Joined(tests), filter, project, Joined(projects), Flag(changed), Count(parallel), Joined(runSettings), configuration, targetFramework, Joined(properties), Flag(noBuild), Flag(includePassed), Count(slowest), Flag(verbose), Count(timeoutSeconds), workspace]),
        force,
        () => context.WithTargetAsync(
            workspace,
            project,
            target =>
            {
                if (SelfBuilt(target, !noBuild && projects is not { Length: > 0 } && Whole(project, configuration)) is { } refused)
                    return Task.FromResult(refused);

                var selection = Selection(test, tests, filter);

                if (!selection.IsOk)
                    return Task.FromResult(selection.Error!.Render());

                var scope = Scoped(configuration, targetFramework, properties);

                if (!scope.IsOk)
                    return Task.FromResult(scope.Error!.Render());

                var degree = Parallelism(parallel);

                if (!degree.IsOk)
                    return Task.FromResult(degree.Error!.Render());

                var settings = Overrides(runSettings);

                if (!settings.IsOk)
                    return Task.FromResult(settings.Error!.Render());

                return TestedAsync(
                    target,
                    project,
                    projects,
                    new TestRunRequest(
                        target.SolutionPath,
                        selection.Value,
                        noBuild,
                        includePassed,
                        slowest,
                        Seconds(timeoutSeconds),
                        verbose,
                        scope.Value,
                        Parallel: degree.Value,
                        RunSettings: settings.Value),
                    changed,
                    cancellationToken);
            },
            changed && WholeSolution(project, projects),
            WholeSolution(project, projects),
            cancellationToken));

    [McpServerTool(Name = "rerun_failed")]
    [Description("Replaces re-running Bash dotnet test --filter by hand. Re-runs only the tests that failed in the previous run_tests call, in the same workspace and target, and by default under the same configuration, targetFramework and properties that run used. tests=[...] and exclude=[...] filter that remembered list instead of replaying it whole, which is how a red round whose expectations the same edit already re-pointed is re-verified selectively; a filtered re-run always ends by naming how many remembered failures it did NOT run. It always runs: it is never answered from the unchanged-run memo, because no argument of the call names the failure list it replays. A green re-run answers in one line, and a build that failed under the re-run returns its error-severity diagnostics only, never its warnings.")]
    public Task<string> RerunFailed(
        [Description("Run existing binaries; skip the build.")] bool noBuild = false,
        [Description("Build configuration, passed to dotnet as -c. Empty reuses the configuration of the run that produced the failures.")] string? configuration = null,
        [Description("Target framework, passed to dotnet as -f. Empty reuses the target framework of the run that produced the failures.")] string? targetFramework = null,
        [Description("MSBuild properties, each written Name=Value and passed to dotnet as -p:Name=Value. Empty reuses the properties of the run that produced the failures.")] string[]? properties = null,
        [Description("Return the full report even when every re-run test passed, and the warnings of a build that failed under the re-run.")] bool verbose = false,
        [Description("Timeout seconds, 1-3600 (600).")] int timeoutSeconds = 600,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Keep only the remembered failures whose name contains one of these entries, case-insensitively, at most 10. Empty replays them all.")] string?[]? tests = null,
        [Description("Drop the remembered failures whose name contains one of these entries, case-insensitively, at most 10. Applied after tests=.")] string?[]? exclude = null,
        CancellationToken cancellationToken = default) =>
        context.WithTargetAsync(workspace, null, target =>
        {
            if (SelfBuilt(target, !noBuild && configuration is not { Length: > 0 }) is { } refused)
                return Task.FromResult(refused);

            var memory = lastRun.Memory;

            if (!memory.Covers(target.Root))
            {
                return Task.FromResult(Errors.Invalid(
                    "no failing test is remembered for this workspace",
                    "call run_tests in this workspace first; a green run leaves nothing to re-run").Render());
            }

            var chosen = Chosen(memory.FailedTests, tests, exclude);

            if (!chosen.IsOk)
                return Task.FromResult(chosen.Error!.Render());

            var scope = Scoped(configuration, targetFramework, properties);

            return scope.IsOk
                ? Noted(
                    RunAsync(
                        target,
                        new TestRunRequest(
                            memory.Target,
                            Rerun(chosen.Value),
                            noBuild,
                            false,
                            0,
                            Seconds(timeoutSeconds),
                            verbose,
                            Remembered(memory.Scope, scope.Value)),
                        cancellationToken),
                    Partial(chosen.Value.Length, memory.FailedTests.Length))
                : Task.FromResult(scope.Error!.Render());
        },
        cancellationToken: cancellationToken);

    [McpServerTool(Name = "list_tests")]
    [Description("Replaces Bash dotnet test --list-tests. Lists the test names a project or solution contains, without running them. A successful listing carries nothing but the names, whatever the build warned about; a build that failed under it returns its error-severity diagnostics only. configuration, targetFramework and properties scope the listing the way they scope build.")]
    public Task<string> ListTests(
        [Description("Substring filter on the name.")] string? contains = null,
        [Description("Project name or path; empty lists every test project.")] string? project = null,
        [Description("Build configuration, passed to dotnet as -c, e.g. Release. Empty uses the SDK default, which is Debug.")] string? configuration = null,
        [Description("Target framework, passed to dotnet as -f, e.g. net10.0. Empty lists every framework a multi-targeted test project declares.")] string? targetFramework = null,
        [Description("MSBuild properties, each written Name=Value and passed to dotnet as -p:Name=Value, e.g. [\"NativeAppHostEnabled=false\"]. Applied after configuration and targetFramework.")] string[]? properties = null,
        [Description("Timeout seconds, 1-3600 (600).")] int timeoutSeconds = 600,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithTargetAsync(workspace, project, target =>
        {
            if (SelfBuilt(target, Whole(project, configuration)) is { } refused)
                return Task.FromResult(refused);

            var scope = Scoped(configuration, targetFramework, properties);

            return scope.IsOk
                ? Contained(target, project, resolved => RecoveredAsync(target, "test listing", async () =>
                {
                    var run = await DotnetRunner.ListTestNamesAsync(
                        target,
                        resolved ?? target.SolutionPath,
                        contains,
                        scope.Value,
                        Seconds(timeoutSeconds),
                        cancellationToken).ConfigureAwait(false);

                    return new LockedRun(run.Response, run.Locked);
                }, cancellationToken))
                : Task.FromResult(scope.Error!.Render());
        },
        cancellationToken: cancellationToken);

    private Task<string> BuildWithRecoveryAsync(
        WorkspaceTarget target,
        string? project,
        BuildScope scope,
        bool verbose,
        CancellationToken cancellationToken) =>
        RecoveredAsync(target, "build", async () =>
        {
            var run = await DotnetRunner.BuildAsync(target, project, scope, verbose, cancellationToken).ConfigureAwait(false);

            return new LockedRun(run.Response, run.Locked);
        }, cancellationToken);

    private Task<string> CleanWithRecoveryAsync(
        WorkspaceTarget target,
        string? project,
        bool includeIntermediate,
        bool dryRun,
        bool verbose,
        CancellationToken cancellationToken) =>
        RecoveredAsync(target, "clean", () =>
        {
            var run = CleanService.Clean(target, project, includeIntermediate, dryRun, verbose, cancellationToken);

            return Task.FromResult(run.IsOk
                ? new LockedRun(run.Value!.Response, run.Value!.Locked)
                : new LockedRun(run.Error!.Render(), false));
        }, cancellationToken);

    private async Task<string> RecoveredAsync(
        WorkspaceTarget target,
        string operation,
        Func<Task<LockedRun>> run,
        CancellationToken cancellationToken)
    {
        var first = await run().ConfigureAwait(false);

        if (!first.Locked)
            return first.Response;

        if (!context.Registry.Unload(target.SolutionPath, reclaim: false))
            return first.Response;

        var second = await run().ConfigureAwait(false);
        var reloaded = await ReloadAsync(target.SolutionPath, cancellationToken).ConfigureAwait(false);

        return second.Response
            + (second.Locked ? StillLocked(operation, second.Response, target.Root) : Recovered(operation))
            + (reloaded ? string.Empty : ReloadFailed);
    }

    private static string Recovered(string operation) => string.Create(
        CultureInfo.InvariantCulture,
        $"\nNOTE the workspace held MSBuild file locks; it was unloaded, the {operation} retried, and the workspace reloaded. Symbol ids are unchanged; undo_last_change history was discarded.");

    internal static string StillLocked(string operation, string output, string root = "")
    {
        var holders = LockHolders.Describe(output, root);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"\nNOTE the workspace was unloaded and the {operation} retried, and the output is still locked. Analyzer and source-generator assemblies are normally mapped from a shadow copy under the per-user analyzer cache rather than from a project's own output, so they are the least likely holder - but a copy that could not be made falls back to mapping the file in place, so they are not ruled out either. This server is pid {Environment.ProcessId}, and an MSBuild BuildHost this or an earlier terse load spawned out of this tree's own bin/ is also in play. {(holders.Length is 0 ? "The build named no holding process, so nothing below identifies one - list the holders yourself before stopping anything." : "Resolve each holder below before stopping it.")}{holders}");
    }

    private const string ReloadFailed =
        "\nWARNING the workspace was unloaded to retry the operation and could not be reloaded; call load_workspace before using the semantic tools again.";

    private async Task<bool> ReloadAsync(string solutionPath, CancellationToken cancellationToken)
    {
        try
        {
            await context.Registry.LoadAsync(solutionPath, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
            or UnauthorizedAccessException or OperationCanceledException)
        {
            return false;
        }
    }

    private Task<string> RunAsync(WorkspaceTarget workspace, TestRunRequest request, CancellationToken cancellationToken) =>
        RecoveredAsync(workspace, "test run", async () =>
        {
            var result = await DotnetRunner.TestAsync(workspace, request, cancellationToken).ConfigureAwait(false);

            lastRun.Remember(
                workspace.Root,
                request.Target,
                result.Report.Failures.Select(failure => failure.Name),
                request.Scope);

            return new LockedRun(result.Response, result.Locked);
        }, cancellationToken);

    internal static Result<string?> Selection(string? test, string?[]? tests, string? filter)
    {
        if (Rejected(tests) is { } refusal)
            return Result.Fail<string?>(refusal);

        var requested = Names(test, tests);

        if (requested.Length > 0 && filter is { Length: > 0 })
        {
            return Result.Fail<string?>(Errors.Invalid(
                "test and filter cannot be combined",
                "pass test= for one name or tests=[...] for several, or filter= for a raw VSTest expression"));
        }

        return requested.Length is 0
            ? Result.Ok(filter)
            : Result.Ok<string?>(string.Join('|', requested.Select(One)));
    }

    private static string Rerun(ImmutableArray<string> failed) => string.Join('|', failed.Select(Exact));

    private static string Exact(string name) => "FullyQualifiedName=" + Escaped(Method(name));

    private static string Method(string name)
    {
        var arguments = name.IndexOf('(', StringComparison.Ordinal);

        return arguments < 0 ? name : name[..arguments];
    }

    private static string Escaped(string name)
    {
        if (name.AsSpan().IndexOfAny(FilterSpecial) < 0)
            return name;

        var text = new StringBuilder(name.Length + 8);

        foreach (var character in name)
            text.Append(FilterSpecial.Contains(character) ? "\\" + character : character.ToString());

        return text.ToString();
    }

    private static TimeSpan Seconds(int timeoutSeconds) => TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 3600));

    private static Task<string> Contained(WorkspaceTarget workspace, string? project, Func<string?, Task<string>> action)
    {
        if (string.IsNullOrWhiteSpace(project))
            return action(null);

        var resolved = workspace.ResolveProject(project);

        return resolved.IsOk ? action(resolved.Value!) : Task.FromResult(resolved.Error!.Render());
    }

    private readonly record struct LockedRun(string Response, bool Locked);

    private static bool IsProperty(ReadOnlySpan<char> property) =>
        property.IndexOf('=') > 0 && property[0] is not '-';

    private static Result<BuildScope> Scoped(string? configuration, string? targetFramework, IReadOnlyList<string>? properties)
    {
        foreach (var property in properties ?? [])
        {
            if (property is null || !IsProperty(property))
            {
                return Result.Fail<BuildScope>(Errors.Invalid(
                    "properties entry " + (property ?? "null") + " is not Name=Value",
                    "pass each MSBuild property as Name=Value, e.g. properties=[\"NativeAppHostEnabled=false\"]"));
            }
        }

        return Result.Ok(new BuildScope(configuration, targetFramework, properties));
    }

    internal static BuildScope Remembered(BuildScope remembered, BuildScope requested) => new(
        requested.Configuration is { Length: > 0 } ? requested.Configuration : remembered.Configuration,
        requested.TargetFramework is { Length: > 0 } ? requested.TargetFramework : remembered.TargetFramework,
        requested.Properties is { Count: > 0 } ? requested.Properties : remembered.Properties);

    private Task<string> SelectedAsync(
    WorkspaceTarget workspace,
    TestRunRequest request,
    bool changed,
    CancellationToken cancellationToken)
    {
        if (!changed)
            return RunAsync(workspace, request, cancellationToken);

        var tests = workspace.Tests;

        if (tests.IsFullRun)
            return Noted(RunAsync(workspace, request, cancellationToken), FullRunNote(tests));

        return Crowded(tests)
            ? Noted(RunAsync(workspace, request, cancellationToken), CrowdedNote(tests))
            : Noted(RunAsync(workspace, request with { Targets = tests.Run }, cancellationToken), SelectedNote(workspace.Root, tests));
    }

    internal static bool Crowded(TestSelection tests) => !tests.IsFullRun && tests.Run.Length > MaxBatchedProjects;

    internal static string CrowdedNote(TestSelection tests) => string.Create(
        CultureInfo.InvariantCulture,
        $"NOTE the change reaches {tests.Run.Length} test projects, more than the {MaxBatchedProjects} a selective run bounds, so every test project of the solution was run instead - the timeout applies per project");

    private static async Task<string> Noted(Task<string> run, string? note)
    {
        var text = await run.ConfigureAwait(false);

        return note is { Length: > 0 } ? text + "\n" + note : text;
    }

    private static string FullRunNote(TestSelection tests) => string.Create(
        CultureInfo.InvariantCulture,
        $"NOTE changed=true ran every test project - {tests.FullRunReason}");

    private static string SelectedNote(string root, TestSelection tests) => string.Create(
        CultureInfo.InvariantCulture,
        $"NOTE changed=true ran {Named(root, tests.Run)}; skipped {Named(root, tests.Skipped)}");

    private static string Named(string root, ImmutableArray<string> projects) => projects.IsDefaultOrEmpty
        ? "nothing"
        : string.Join(", ", projects.Select(path => PositionFormat.Relative(root, path)));

    private static string CleanUnloaded(string path, bool includeIntermediate, bool dryRun, bool verbose, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(path);

        if (!File.Exists(full))
        {
            return Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{path}' does not exist"),
                "pass an existing .slnx, .sln, .slnf or project file, or drop path= to clean the loaded workspace").Render();
        }

        if (!SolutionFile.IsSolutionFile(full) && !SolutionFile.IsProjectFile(full))
        {
            return Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{path}' is not a solution or project file"),
                "path= takes a .slnx, .sln, .slnf, .csproj, .fsproj or .vbproj that is not loaded").Render();
        }

        var target = new WorkspaceTarget(full, Path.GetDirectoryName(full)!);
        var run = CleanService.Clean(target, null, includeIntermediate, dryRun, verbose, cancellationToken);

        return run.IsOk ? run.Value!.Response : run.Error!.Render();
    }

    private static Result<ImmutableArray<string>> ResolvedProjects(WorkspaceTarget workspace, string?[] projects)
    {
        if (projects.Length > MaxBatchedProjects)
            return Result.Fail<ImmutableArray<string>>(TooManyProjects(projects.Length));

        var resolved = ImmutableArray.CreateBuilder<string>();

        foreach (var entry in projects)
        {
            var found = Resolved(workspace, entry, resolved);

            if (!found.IsOk)
                return Result.Fail<ImmutableArray<string>>(found.Error!);

            resolved.Add(found.Value!);
        }

        return Result.Ok(resolved.DrainToImmutable());
    }

    internal const int MaxBatchedProjects = 10;

    private Task<string> TestedAsync(
        WorkspaceTarget target,
        string? project,
        string?[]? projects,
        TestRunRequest request,
        bool changed,
        CancellationToken cancellationToken)
    {
        if (projects is not { Length: > 0 } named)
        {
            return Contained(target, project, resolved => SelectedAsync(
                target,
                request with { Target = resolved ?? target.SolutionPath },
                resolved is null && changed,
                cancellationToken));
        }

        if (project is { Length: > 0 })
        {
            return Task.FromResult(Errors.Invalid(
                "projects= was passed together with project=, and one of them would have been silently dropped",
                "pass every project in projects=, or the single one in project=").Render());
        }

        var resolved = ResolvedProjects(target, named);

        return resolved.IsOk
            ? RunAsync(target, request with { Target = target.SolutionPath, Targets = resolved.Value }, cancellationToken)
            : Task.FromResult(resolved.Error!.Render());
    }

    private const int MaxParallel = TestRunRequest.MaxParallel;

    private static Result<int> Parallelism(int parallel) => parallel is < 0 or > MaxParallel
    ? Result.Fail<int>(Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"parallel was {parallel}, outside the accepted range 0-{MaxParallel}"),
        string.Create(CultureInfo.InvariantCulture, $"pass 0 for one process per core, 1 to run the projects one at a time, or up to {MaxParallel}")))
    : Result.Ok(parallel);

    private static TerseError TooManyProjects(int count) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"projects carried {count} entries, at most {MaxBatchedProjects} run in one call"),
        string.Create(CultureInfo.InvariantCulture, $"send at most {MaxBatchedProjects} per call - the timeout applies to each project, so a longer batch has no bound"));

    private static Result<string> Resolved(WorkspaceTarget workspace, string? entry, ImmutableArray<string>.Builder taken)
    {
        if (entry is not { Length: > 0 })
            return Result.Fail<string>(Errors.Invalid("'projects' carries a blank entry", "drop it, or name the project you meant to run"));

        var found = workspace.ResolveProject(entry);

        return found.IsOk && taken.Contains(found.Value!, StringComparer.OrdinalIgnoreCase)
            ? Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"projects names {Path.GetFileName(found.Value.AsSpan())} twice, as '{entry}'"),
                "drop the duplicate - two invocations of one test assembly run concurrently against the same output and fail tests that pass on their own"))
            : found;
    }

    private static Result<ImmutableArray<string>> Overrides(string[]? runSettings)
    {
        foreach (var setting in runSettings ?? [])
        {
            if (setting is null || !IsProperty(setting))
            {
                return Result.Fail<ImmutableArray<string>>(Errors.Invalid(
                    "runSettings entry " + (setting ?? "null") + " is not Name=Value",
                    "pass each RunSettings override as Name=Value, e.g. runSettings=[\"xUnit.MaxParallelThreads=1\"]"));
            }
        }

        return Result.Ok(runSettings is null ? ImmutableArray<string>.Empty : [.. runSettings]);
    }

    private static bool WholeSolution(string? project, string?[]? projects) =>
        string.IsNullOrWhiteSpace(project) && projects is not { Length: > 0 };

    private static string? SelfBuilt(WorkspaceTarget target, bool writes) => SelfBuild.Refusal(target, writes)?.Render();

    private static bool Whole(string? project, string? configuration) =>
        project is not { Length: > 0 } && configuration is not { Length: > 0 };

    private const int MaxTests = 10;

    private static TerseError? Rejected(string?[]? tests, string parameter = "tests")
    {
        if (tests is not { Length: > 0 } entries)
            return null;

        if (entries.Length > MaxTests)
        {
            return Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"{parameter} carried {entries.Length} entries"),
                string.Create(CultureInfo.InvariantCulture, $"pass at most {MaxTests} test names, or filter= for one raw VSTest expression"));
        }

        var blank = Array.FindIndex(entries, entry => entry is not { Length: > 0 });

        return blank < 0
            ? null
            : Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"{parameter}[{blank}] is blank"),
                string.Create(CultureInfo.InvariantCulture, $"remove the blank entry from {parameter}="));
    }

    private static string[] Names(string? single, string?[]? many) =>
    [
        .. single is { Length: > 0 } ? [single] : (string[])[],
    .. (many ?? []).Where(name => name is { Length: > 0 }).Select(name => name!),
];

    private static string One(string name) => name.Contains('(', StringComparison.Ordinal)
        ? Exact(name)
        : "FullyQualifiedName~" + Escaped(name);

    private static Result<ImmutableArray<string>> Chosen(ImmutableArray<string> failed, string?[]? tests, string?[]? exclude)
    {
        var refusal = Rejected(tests) ?? Rejected(exclude, "exclude");

        if (refusal is not null)
            return Result.Fail<ImmutableArray<string>>(refusal);

        var kept = Filtered(failed, Names(null, tests), Names(null, exclude));

        return kept.IsEmpty
            ? Result.Fail<ImmutableArray<string>>(NothingMatched(failed.Length))
            : Result.Ok(kept);
    }

    private static ImmutableArray<string> Filtered(ImmutableArray<string> failed, string[] kept, string[] dropped) =>
        kept.Length is 0 && dropped.Length is 0
            ? failed
            : [.. failed.Where(name => Wanted(name, kept, dropped))];

    private static bool Wanted(string name, string[] kept, string[] dropped) =>
        (kept.Length is 0 || Array.Exists(kept, entry => name.Contains(entry, StringComparison.OrdinalIgnoreCase)))
        && !Array.Exists(dropped, entry => name.Contains(entry, StringComparison.OrdinalIgnoreCase));

    private static TerseError NothingMatched(int remembered) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"no remembered failure matched tests= or survived exclude=: {remembered} remembered"),
        "widen tests=, drop exclude=, or call rerun_failed with neither to replay every remembered failure");

    private static string? Partial(int chosen, int remembered) => chosen == remembered
        ? null
        : string.Create(CultureInfo.InvariantCulture, $"NOTE partial rerun - {chosen} of {remembered} remembered failure(s) re-run; the {remembered - chosen} not named here were NOT verified");

    private const string GreenVerdict = "run_tests PASSED";

    private Task<string> Replayable(string key, bool force, Func<Task<string>> run)
    {
        var stamp = Stamp();
        var now = Stopwatch.GetTimestamp();

        return !force && unchanged.Replay(key, stamp, now) is { } previous
            ? Task.FromResult(previous)
            : Memoized(key, stamp, run());
    }

    private async Task<string> Memoized(string key, string stamp, Task<string> run)
    {
        var text = await run.ConfigureAwait(false);

        if (text.StartsWith(GreenVerdict, StringComparison.Ordinal) && !text.Contains('\n', StringComparison.Ordinal))
            unchanged.Remember(key, stamp, text, Stopwatch.GetTimestamp());

        return text;
    }

    private string Stamp()
    {
        var loaded = context.Registry.All();
        var stamp = new StringBuilder(128);

        stamp.Append(CultureInfo.InvariantCulture, $"pulse={EditPulse.Changed} loaded={loaded.Count}");

        foreach (var workspace in loaded.OrderBy(entry => entry.Root, StringComparer.Ordinal))
        {
            stamp.Append(' ')
                .Append(workspace.Root)
                .Append('@')
                .Append(workspace.LoadedUtc.ToString("O", CultureInfo.InvariantCulture))
                .Append('=')
                .Append(workspace.Sync.Generations.ToString());
        }

        return stamp.ToString();
    }

    private static string Key(string tool, params ReadOnlySpan<string?> parts)
    {
        var key = new StringBuilder(tool, 128);

        foreach (var part in parts)
            key.Append(KeySeparator).Append(part);

        return key.ToString();
    }

    private static string Flag(bool value) => value ? "1" : "0";

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Joined(IReadOnlyList<string?>? values) =>
        values is { Count: > 0 } ? string.Join(',', values) : string.Empty;

    private static readonly string KeySeparator = new((char)31, 1);
}
