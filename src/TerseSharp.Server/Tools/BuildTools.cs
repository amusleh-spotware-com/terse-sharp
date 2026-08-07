using System.Buffers;
using System.Text;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class BuildTools(ToolContext context, LastTestRun lastRun)
{
    private static readonly SearchValues<char> FilterSpecial = SearchValues.Create("\\()&|=!~");

    [McpServerTool(Name = "build")]
    [Description("Replaces Bash dotnet build. A successful build answers in one line - warnings are counted, never listed - and a failed build lists error-severity diagnostics only. Raw MSBuild output is never returned. Pass verbose=true for every diagnostic of every severity, configuration=Release to build a non-default configuration, targetFramework to build one framework of a multi-targeted project, and properties for MSBuild properties such as NativeAppHostEnabled=false.")]
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
            var scope = Scoped(configuration, targetFramework, properties);

            return scope.IsOk
                ? Contained(target, project, resolved => BuildWithRecoveryAsync(
                    target, resolved, scope.Value, verbose, cancellationToken))
                : Task.FromResult(scope.Error!.Render());
        });

    [McpServerTool(Name = "clean")]
    [Description("Replaces Bash dotnet clean. Deletes the bin and obj directories of the workspace or of one project and reports how many files and bytes were freed, never raw MSBuild output. Unlike dotnet clean it also removes obj, and when the loaded workspace's own MSBuild file locks block the delete it unloads, retries and reloads. A clean with nothing locked reports counters only; verbose=true adds the per-directory list. Not covered by undo_last_change.")]
    public Task<string> Clean(
        [Description("Project path; empty cleans every project under the workspace root.")] string? project = null,
        [Description("Also delete obj, the intermediate output. Default true; false leaves obj as dotnet clean does.")] bool includeIntermediate = true,
        [Description("List what would be deleted and delete nothing.")] bool dryRun = false,
        [Description("List every directory, not just the counters.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default)
    {
        var rejection = context.RejectWrite();

        return rejection is not null
            ? Task.FromResult(rejection)
            : context.WithTargetAsync(workspace, project, target => CleanWithRecoveryAsync(
                target, project, includeIntermediate, dryRun, verbose, cancellationToken));
    }

    [McpServerTool(Name = "run_tests")]
    [Description("Replaces Bash dotnet test. A green run answers in one line - passed/skipped/total/durationMs - so a passing suite costs almost nothing; a test failure returns each failure's message, expected and actual values, and one source frame, and a build that failed under the run returns its error-severity diagnostics only, never its warnings. Pass verbose=true to get the full report on a green run, and the hidden warnings on a failed build; configuration, targetFramework and properties scope the run the way they scope build, so noBuild=true against a Release tree tests the Release binaries.")]
    public Task<string> RunTests(
        [Description("Optional test to run: a fully-qualified test name, or a class or namespace prefix. Cannot be combined with filter.")] string? test = null,
        [Description("Optional VSTest filter expression. Cannot be combined with test.")] string? filter = null,
        [Description("Project path; empty runs every test project.")] string? project = null,
        [Description("Build configuration, passed to dotnet as -c, e.g. Release. Empty uses the SDK default, which is Debug.")] string? configuration = null,
        [Description("Target framework, passed to dotnet as -f, e.g. net10.0. Empty runs every framework a multi-targeted test project declares.")] string? targetFramework = null,
        [Description("MSBuild properties, each written Name=Value and passed to dotnet as -p:Name=Value, e.g. [\"NativeAppHostEnabled=false\"]. Applied after configuration and targetFramework.")] string[]? properties = null,
        [Description("Run existing binaries; skip the build.")] bool noBuild = false,
        [Description("List passing tests too.")] bool includePassed = false,
        [Description("List the N slowest tests.")] int slowest = 0,
        [Description("Return the full report even when every test passed, and the warnings of a build that failed under the run. Default false, which answers a green run in one line and reports errors only.")] bool verbose = false,
        [Description("Timeout seconds, 1-3600 (600).")] int timeoutSeconds = 600,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithTargetAsync(workspace, project, target =>
        {
            var selection = Selection(test, filter);

            if (!selection.IsOk)
                return Task.FromResult(selection.Error!.Render());

            var scope = Scoped(configuration, targetFramework, properties);

            if (!scope.IsOk)
                return Task.FromResult(scope.Error!.Render());

            return Contained(target, project, resolved => RunAsync(
                target,
                new TestRunRequest(
                    resolved ?? target.SolutionPath,
                    selection.Value,
                    noBuild,
                    includePassed,
                    slowest,
                    Seconds(timeoutSeconds),
                    verbose,
                    scope.Value),
                cancellationToken));
        });

    [McpServerTool(Name = "rerun_failed")]
    [Description("Replaces re-running Bash dotnet test --filter by hand. Re-runs only the tests that failed in the previous run_tests call, in the same workspace and target, and by default under the same configuration, targetFramework and properties that run used. A green re-run answers in one line, and a build that failed under the re-run returns its error-severity diagnostics only, never its warnings.")]
    public Task<string> RerunFailed(
        [Description("Run existing binaries; skip the build.")] bool noBuild = false,
        [Description("Build configuration, passed to dotnet as -c. Empty reuses the configuration of the run that produced the failures.")] string? configuration = null,
        [Description("Target framework, passed to dotnet as -f. Empty reuses the target framework of the run that produced the failures.")] string? targetFramework = null,
        [Description("MSBuild properties, each written Name=Value and passed to dotnet as -p:Name=Value. Empty reuses the properties of the run that produced the failures.")] string[]? properties = null,
        [Description("Return the full report even when every re-run test passed, and the warnings of a build that failed under the re-run.")] bool verbose = false,
        [Description("Timeout seconds, 1-3600 (600).")] int timeoutSeconds = 600,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithTargetAsync(workspace, null, target =>
        {
            var memory = lastRun.Memory;

            if (!memory.Covers(target.Root))
            {
                return Task.FromResult(Errors.Invalid(
                    "no failing test is remembered for this workspace",
                    "call run_tests in this workspace first; a green run leaves nothing to re-run").Render());
            }

            var scope = Scoped(configuration, targetFramework, properties);

            return scope.IsOk
                ? RunAsync(
                    target,
                    new TestRunRequest(
                        memory.Target,
                        Rerun(memory),
                        noBuild,
                        false,
                        0,
                        Seconds(timeoutSeconds),
                        verbose,
                        Remembered(memory.Scope, scope.Value)),
                    cancellationToken)
                : Task.FromResult(scope.Error!.Render());
        });

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
        });

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

        if (context.Registry.All().Count is not 1)
            return first.Response + NotRecovered(operation);

        if (!context.Registry.Unload(target.SolutionPath, reclaim: false))
            return first.Response;

        var second = await run().ConfigureAwait(false);
        var reloaded = await ReloadAsync(target.SolutionPath, cancellationToken).ConfigureAwait(false);

        return second.Response
            + (second.Locked ? StillLocked(operation) : Recovered(operation))
            + (reloaded ? string.Empty : ReloadFailed);
    }

    private static string Recovered(string operation) => string.Create(
        CultureInfo.InvariantCulture,
        $"\nNOTE the workspace held MSBuild file locks; it was unloaded, the {operation} retried, and the workspace reloaded. Symbol ids are unchanged; undo_last_change history was discarded.");

    private static string StillLocked(string operation) => string.Create(
    CultureInfo.InvariantCulture,
    $"\nNOTE the workspace was unloaded and the {operation} retried, and the output is still locked, so something outside this server holds it. Analyzer and source-generator assemblies are loaded from a shadow copy under the temp directory and never map a project's own output, so this is not the workspace. Stop whatever else holds the file - this server is pid {Environment.ProcessId} - then try again.");

    private static string NotRecovered(string operation) => string.Create(
        CultureInfo.InvariantCulture,
        $"\nNOTE the output is locked, and more than one workspace is loaded, so the {operation} was not retried; unload_workspace the one you are targeting and try again.");

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

    private static Result<string?> Selection(string? test, string? filter)
    {
        if (test is { Length: > 0 } && filter is { Length: > 0 })
        {
            return Result.Fail<string?>(Errors.Invalid(
                "test and filter cannot be combined",
                "pass test= for one test, class or namespace, or filter= for a raw VSTest expression"));
        }

        if (test is not { Length: > 0 } name)
            return Result.Ok(filter);

        return Result.Ok<string?>(name.Contains('(', StringComparison.Ordinal)
            ? Exact(name)
            : "FullyQualifiedName~" + Escaped(name));
    }

    private static string Rerun(TestRunMemory memory) => string.Join('|', memory.FailedTests.Select(Exact));

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
}
