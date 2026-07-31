using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class WorkspaceTools(ToolContext context)
{
    [McpServerTool(Name = "load_workspace")]
    [Description("Load a .sln/.slnx/.slnf/.csproj into memory. Call once per solution; every other tool needs it. Pass no path to auto-discover from the current directory.")]
    public async Task<string> LoadWorkspace(
        [Description("Path to the solution or project. Empty = discover upwards from the working directory.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(path) ? Discover() : path;

        if (target is null)
            return Errors.Invalid("no solution or project found", "pass an explicit path").Render();

        var result = await context.Registry.LoadAsync(target, cancellationToken).ConfigureAwait(false);

        return Render(result);
    }

    [McpServerTool(Name = "list_workspaces")]
    [Description("List loaded workspaces with their git branch and worktree, so you can disambiguate several checkouts of one repo.")]
    public async Task<string> ListWorkspaces()
    {
        await context.ReadyAsync().ConfigureAwait(false);

        var all = context.Registry.All();
        var response = new ResponseBuilder("list_workspaces", string.Empty);

        response.Summary(all.Count, all.Count, "workspaces");

        foreach (var workspace in all)
            response.Line(Describe(workspace));

        if (all.Count is 0 && context.PreloadFailure is { } failure)
            response.Note("the startup preload failed: " + failure);

        return response.ToString();
    }

    [McpServerTool(Name = "unload_workspace")]
    [Description("Unload a workspace and release its MSBuild file locks so an external build can run.")]
    public async Task<string> UnloadWorkspace([Description("Solution or project path to unload.")] string path)
    {
        await context.ReadyAsync().ConfigureAwait(false);

        return context.Registry.Unload(path) ? "unloaded " + path : "not loaded " + path;
    }

    [McpServerTool(Name = "workspace_status")]
    [Description("Report a loaded workspace: solution, git worktree and branch, project and document counts, load time, and any project that failed to load.")]
    public Task<string> WorkspaceStatus([Description("Optional workspace path or worktree name.")] string? workspace = null) =>
        context.WithWorkspace(workspace, null, RenderStatus);

    [McpServerTool(Name = "list_projects")]
    [Description("List the projects of a loaded workspace: name, target framework, document count.")]
    public Task<string> ListProjects([Description("Optional workspace path or worktree name.")] string? workspace = null) =>
        context.WithWorkspace(workspace, null, RenderProjects);

    private static string? Discover() =>
        WorkspaceDiscovery.Find(Directory.GetCurrentDirectory()) is [var first, ..] ? first : null;

    private static string RenderStatus(LoadedWorkspace workspace)
    {
        var response = new ResponseBuilder("workspace_status", workspace.SolutionPath);

        response.Note(Describe(workspace));
        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"documents={workspace.Load.DocumentCount} loadMs={workspace.Load.ElapsedMilliseconds} lastUsedUtc={workspace.LastUsedUtc:O}"));

        foreach (var failure in workspace.Load.Failures)
            response.Line("FAILED " + failure);

        return response.ToString();
    }

    private static string RenderProjects(LoadedWorkspace workspace)
    {
        var projects = workspace.Solution.Projects.OrderBy(project => project.Name, StringComparer.Ordinal).ToArray();
        var response = new ResponseBuilder("list_projects", workspace.SolutionPath);

        response.Summary(projects.Length, projects.Length, "projects");

        foreach (var project in projects)
        {
            response.Line(string.Create(
                CultureInfo.InvariantCulture,
                $"{project.Name}  {project.Language}  documents={project.Documents.Count()}"));
        }

        return response.ToString();
    }

    private static string Describe(LoadedWorkspace workspace) => string.Create(
        CultureInfo.InvariantCulture,
        $"{workspace.SolutionPath}  worktree={workspace.Git.WorktreeName} branch={workspace.Git.Branch}  projects={workspace.Load.ProjectCount}");

    private static string Render(WorkspaceLoadResult result)
    {
        var response = new ResponseBuilder("load_workspace", result.SolutionPath);

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"projects={result.ProjectCount} documents={result.DocumentCount} elapsedMs={result.ElapsedMilliseconds} failures={result.Failures.Count}"));

        foreach (var failure in result.Failures)
            response.Line("FAILED " + failure);

        return response.ToString();
    }
}
