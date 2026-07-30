using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class BuildTools(ToolContext context)
{
    [McpServerTool(Name = "build")]
    [Description("Build the workspace and return deduplicated diagnostics only, never raw MSBuild output.")]
    public Task<string> Build(
        [Description("Optional project path. Empty builds the whole solution.")] string? project = null,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, project, loaded =>
            Contained(loaded, project, resolved => DotnetRunner.BuildAsync(loaded, resolved, cancellationToken)));

    [McpServerTool(Name = "run_tests")]
    [Description("Run tests and return failures only: name, message and the assertion frame. A green run is one summary line.")]
    public Task<string> RunTests(
        [Description("Optional VSTest filter expression.")] string? filter = null,
        [Description("Optional project path.")] string? project = null,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, project, loaded =>
            Contained(loaded, project, resolved => DotnetRunner.TestAsync(loaded, resolved, filter, cancellationToken)));

    private static Task<string> Contained(LoadedWorkspace workspace, string? project, Func<string?, Task<string>> action)
    {
        if (string.IsNullOrWhiteSpace(project))
            return action(null);

        var resolved = PathGuard.Resolve(workspace, project);

        return resolved.IsOk ? action(resolved.Value) : Task.FromResult(resolved.Error!.Render());
    }
}
