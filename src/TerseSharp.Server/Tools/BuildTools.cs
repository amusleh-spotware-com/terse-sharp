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
            DotnetRunner.BuildAsync(loaded, project, cancellationToken));

    [McpServerTool(Name = "run_tests")]
    [Description("Run tests and return failures only: name, message and the assertion frame. A green run is one summary line.")]
    public Task<string> RunTests(
        [Description("Optional VSTest filter expression.")] string? filter = null,
        [Description("Optional project path.")] string? project = null,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, project, loaded =>
            DotnetRunner.TestAsync(loaded, project, filter, cancellationToken));
}
