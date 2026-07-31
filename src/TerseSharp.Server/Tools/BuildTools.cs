using System.Buffers;
using System.Text;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class BuildTools(ToolContext context, LastTestRun lastRun)
{
    private static readonly SearchValues<char> FilterSpecial = SearchValues.Create("\\()&|=!~");

    private const string Recovered =
        "\nNOTE the workspace held MSBuild file locks; it was unloaded, the build retried, and the workspace reloaded. Symbol ids are unchanged; undo_last_change history was discarded.";

    private const string StillLocked =
        "\nNOTE the workspace was unloaded and the build retried, and the output is still locked. The holder is another running process that owns the file, not the workspace - stop it and build again.";

    private const string ReloadFailed =
        "\nWARNING the workspace was unloaded to retry the build and could not be reloaded; call load_workspace before using the semantic tools again.";

    private const string NotRecovered =
        "\nNOTE the output is locked, and more than one workspace is loaded, so the build was not retried; unload_workspace the one you are building and try again.";

    [McpServerTool(Name = "build")]
    [Description("Build the workspace and return deduplicated diagnostics only, never raw MSBuild output.")]
    public Task<string> Build(
        [Description("Project path; empty builds the solution.")] string? project = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithTargetAsync(workspace, project, target =>
            Contained(target, project, resolved => BuildWithRecoveryAsync(target, resolved, cancellationToken)));

    [McpServerTool(Name = "run_tests")]
    [Description("Replaces Bash dotnet test. Returns passed/failed/skipped/total counters plus every failure with its message, expected and actual values, and one source frame - never the raw runner output.")]
    public Task<string> RunTests(
        [Description("Optional test to run: a fully-qualified test name, or a class or namespace prefix. Cannot be combined with filter.")] string? test = null,
        [Description("Optional VSTest filter expression. Cannot be combined with test.")] string? filter = null,
        [Description("Project path; empty runs every test project.")] string? project = null,
        [Description("Run existing binaries; skip the build.")] bool noBuild = false,
        [Description("List passing tests too.")] bool includePassed = false,
        [Description("List the N slowest tests.")] int slowest = 0,
        [Description("Timeout seconds, 1-3600 (600).")] int timeoutSeconds = 600,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithTargetAsync(workspace, project, target =>
        {
            var selection = Selection(test, filter);

            return selection.IsOk
                ? Contained(target, project, resolved => RunAsync(
                    target,
                    new TestRunRequest(resolved ?? target.SolutionPath, selection.Value, noBuild, includePassed, slowest, Seconds(timeoutSeconds)),
                    cancellationToken))
                : Task.FromResult(selection.Error!.Render());
        });

    [McpServerTool(Name = "rerun_failed")]
    [Description("Replaces re-running Bash dotnet test --filter by hand. Re-runs only the tests that failed in the previous run_tests call, in the same workspace and target.")]
    public Task<string> RerunFailed(
        [Description("Run existing binaries; skip the build.")] bool noBuild = false,
        [Description("Timeout seconds, 1-3600 (600).")] int timeoutSeconds = 600,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithTargetAsync(workspace, null, target =>
        {
            var memory = lastRun.Memory;

            return memory.Covers(target.Root)
                ? RunAsync(target, new TestRunRequest(memory.Target, Rerun(memory), noBuild, false, 0, Seconds(timeoutSeconds)), cancellationToken)
                : Task.FromResult(Errors.Invalid(
                    "no failing test is remembered for this workspace",
                    "call run_tests in this workspace first; a green run leaves nothing to re-run").Render());
        });

    [McpServerTool(Name = "list_tests")]
    [Description("Replaces Bash dotnet test --list-tests. Lists the test names a project or solution contains, without running them.")]
    public Task<string> ListTests(
        [Description("Substring filter on the name.")] string? contains = null,
        [Description("Project path; empty lists every test project.")] string? project = null,
        [Description("Timeout seconds, 1-3600 (600).")] int timeoutSeconds = 600,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithTargetAsync(workspace, project, target =>
            Contained(target, project, resolved => DotnetRunner.ListTestsAsync(
                target,
                resolved ?? target.SolutionPath,
                contains,
                Seconds(timeoutSeconds),
                cancellationToken)));

    private async Task<string> BuildWithRecoveryAsync(WorkspaceTarget target, string? project, CancellationToken cancellationToken)
    {
        var first = await DotnetRunner.BuildAsync(target, project, cancellationToken).ConfigureAwait(false);

        if (!first.Locked)
            return first.Response;

        if (context.Registry.All().Count is not 1)
            return first.Response + NotRecovered;

        if (!context.Registry.Unload(target.SolutionPath))
            return first.Response;

        var second = await DotnetRunner.BuildAsync(target, project, cancellationToken).ConfigureAwait(false);

        var reloaded = await ReloadAsync(target.SolutionPath, cancellationToken).ConfigureAwait(false);

        return second.Response + (second.Locked ? StillLocked : Recovered) + (reloaded ? string.Empty : ReloadFailed);
    }

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

    private async Task<string> RunAsync(WorkspaceTarget workspace, TestRunRequest request, CancellationToken cancellationToken)
    {
        var result = await DotnetRunner.TestAsync(workspace, request, cancellationToken).ConfigureAwait(false);

        lastRun.Remember(workspace.Root, request.Target, result.Report.Failures.Select(failure => failure.Name));

        return result.Response;
    }

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

        var resolved = PathGuard.Resolve(workspace.Root, project);

        return resolved.IsOk ? action(resolved.Value!) : Task.FromResult(resolved.Error!.Render());
    }
}
