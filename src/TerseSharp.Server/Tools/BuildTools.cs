using System.Buffers;
using System.Text;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class BuildTools(ToolContext context, LastTestRun lastRun)
{
    private static readonly SearchValues<char> FilterSpecial = SearchValues.Create("\\()&|=!~");

    [McpServerTool(Name = "build")]
    [Description("Build the workspace and return deduplicated diagnostics only, never raw MSBuild output.")]
    public Task<string> Build(
        [Description("Optional project path. Empty builds the whole solution.")] string? project = null,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, project, loaded =>
            Contained(loaded, project, resolved => DotnetRunner.BuildAsync(loaded, resolved, cancellationToken)));

    [McpServerTool(Name = "run_tests")]
    [Description("Replaces Bash dotnet test. Returns passed/failed/skipped/total counters plus every failure with its message, expected and actual values, and one source frame - never the raw runner output.")]
    public Task<string> RunTests(
        [Description("Optional test to run: a fully-qualified test name, or a class or namespace prefix. Cannot be combined with filter.")] string? test = null,
        [Description("Optional VSTest filter expression. Cannot be combined with test.")] string? filter = null,
        [Description("Optional project path. Empty runs every test project in the solution.")] string? project = null,
        [Description("Skip the build and run the existing binaries. Default false.")] bool noBuild = false,
        [Description("List the passing tests as well. Default false.")] bool includePassed = false,
        [Description("List the N slowest tests. Default 0.")] int slowest = 0,
        [Description("Timeout in seconds, clamped to 1-3600. Default 600.")] int timeoutSeconds = 600,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, project, loaded =>
        {
            var selection = Selection(test, filter);

            return selection.IsOk
                ? Contained(loaded, project, resolved => RunAsync(
                    loaded,
                    new TestRunRequest(resolved ?? loaded.SolutionPath, selection.Value, noBuild, includePassed, slowest, Seconds(timeoutSeconds)),
                    cancellationToken))
                : Task.FromResult(selection.Error!.Render());
        });

    [McpServerTool(Name = "rerun_failed")]
    [Description("Replaces re-running Bash dotnet test --filter by hand. Re-runs only the tests that failed in the previous run_tests call, in the same workspace and target.")]
    public Task<string> RerunFailed(
        [Description("Skip the build and run the existing binaries. Default false.")] bool noBuild = false,
        [Description("Timeout in seconds, clamped to 1-3600. Default 600.")] int timeoutSeconds = 600,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, null, loaded =>
        {
            var memory = lastRun.Memory;

            return memory.Covers(loaded.Root)
                ? RunAsync(loaded, new TestRunRequest(memory.Target, Rerun(memory), noBuild, false, 0, Seconds(timeoutSeconds)), cancellationToken)
                : Task.FromResult(Errors.Invalid(
                    "no failing test is remembered for this workspace",
                    "call run_tests in this workspace first; a green run leaves nothing to re-run").Render());
        });

    [McpServerTool(Name = "list_tests")]
    [Description("Replaces Bash dotnet test --list-tests. Lists the test names a project or solution contains, without running them.")]
    public Task<string> ListTests(
        [Description("Optional substring; only names containing it are kept.")] string? contains = null,
        [Description("Optional project path. Empty lists every test project in the solution.")] string? project = null,
        [Description("Timeout in seconds, clamped to 1-3600. Default 600.")] int timeoutSeconds = 600,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, project, loaded =>
            Contained(loaded, project, resolved => DotnetRunner.ListTestsAsync(
                loaded,
                resolved ?? loaded.SolutionPath,
                contains,
                Seconds(timeoutSeconds),
                cancellationToken)));

    private async Task<string> RunAsync(LoadedWorkspace workspace, TestRunRequest request, CancellationToken cancellationToken)
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

    private static Task<string> Contained(LoadedWorkspace workspace, string? project, Func<string?, Task<string>> action)
    {
        if (string.IsNullOrWhiteSpace(project))
            return action(null);

        var resolved = PathGuard.Resolve(workspace, project);

        return resolved.IsOk ? action(resolved.Value!) : Task.FromResult(resolved.Error!.Render());
    }
}
