using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class WorkspaceTools(ToolContext context)
{
    [McpServerTool(Name = "load_workspace")]
    [Description("Load a .sln/.slnx/.slnf/.csproj into memory. Call once per solution; every other tool needs it. Pass no path to auto-discover from the current directory, or discover=true to list what a directory contains without loading it. External edits are picked up automatically, so reload=true is only for a change the server cannot see.")]
    public Task<string> LoadWorkspace(
        [Description("Path to the solution or project. Empty = discover upwards from the working directory.")] string? path = null,
        [Description("Discard the in-memory solution and read it from disk again. Generation counters carry over and the undo history is cleared.")] bool reload = false,
        [Description("List the MSBuild warnings the load reported, not just their count. Default false.")] bool verbose = false,
        [Description("List every .slnx/.sln/.slnf/.csproj under path without loading anything. Use before the first load when you do not know what a repository contains.")] bool discover = false,
        [Description("Max candidates when discover=true (100).")] int maxResults = 0,
        CancellationToken cancellationToken = default) =>
        ToolBoundary.RunAsync(async () =>
        {
            if (discover)
                return WorkspaceDiscovery.Discover(path ?? Directory.GetCurrentDirectory(), NavigationTools.Cap(maxResults, 100));

            var target = string.IsNullOrWhiteSpace(path) ? Discover() : path;

            if (target is null)
                return Errors.Invalid("no solution or project found", "pass an explicit path").Render();

            var result = reload
                ? await context.Registry.ReloadAsync(target, cancellationToken).ConfigureAwait(false)
                : await context.Registry.LoadAsync(target, cancellationToken).ConfigureAwait(false);

            return Render(result, verbose);
        });

    [McpServerTool(Name = "list_workspaces")]
    [Description("List loaded workspaces with their git branch and worktree, so you can disambiguate several checkouts of one repo.")]
    public Task<string> ListWorkspaces() =>
        ToolBoundary.RunAsync(async () =>
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
        });

    [McpServerTool(Name = "unload_workspace")]
    [Description("Unload a workspace and release its MSBuild file locks so an external build can run.")]
    public Task<string> UnloadWorkspace([Description("Solution or project path to unload.")] string path) =>
        ToolBoundary.RunAsync(async () =>
        {
            await context.ReadyAsync().ConfigureAwait(false);

            var response = new ResponseBuilder("unload_workspace", path);

            response.Note(context.Registry.Unload(path) ? "unloaded" : "not loaded");

            return response.ToString();
        });

    [McpServerTool(Name = "workspace_status")]
    [Description("Report a loaded workspace: solution, git worktree and branch, project and document counts, load time, and any project that failed to load.")]
    public Task<string> WorkspaceStatus(
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("List the MSBuild warnings the load reported, not just their count. Default false.")] bool verbose = false,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(
            workspace,
            null,
            loaded => RenderStatusAsync(loaded, verbose, cancellationToken),
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "list_projects")]
    [Description("List the projects of a loaded workspace: name, target framework, document count.")]
    public Task<string> ListProjects([Description("Workspace or worktree name.")] string? workspace = null) =>
        context.WithWorkspace(workspace, null, RenderProjects);

    private static string? Discover() =>
        WorkspaceDiscovery.Find(Directory.GetCurrentDirectory()) is [var first, ..] ? first : null;

    private static async Task<string> RenderStatusAsync(LoadedWorkspace workspace, bool verbose, CancellationToken cancellationToken)
    {
        var response = new ResponseBuilder("workspace_status", workspace.SolutionPath);

        response.Note(Describe(workspace));
        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"documents={workspace.Load.DocumentCount} loadMs={workspace.Load.ElapsedMilliseconds} failures={workspace.Load.Failures.Count} warnings={workspace.Load.Warnings.Count} lastUsedUtc={workspace.LastUsedUtc:O}"));
        response.Note(DescribeSync(workspace.Sync));
        response.Note(workspace.Indexes.Describe());

        if (workspace.Indexes.Razor().FileCount is var razor and > 0)
            response.Note(await RazorHealthAsync(workspace, razor, cancellationToken).ConfigureAwait(false));

        AppendLoadDiagnostics(response, workspace.Load, verbose);

        return response.ToString();
    }

    private static async Task<string> RazorHealthAsync(LoadedWorkspace workspace, int razor, CancellationToken cancellationToken)
    {
        var ran = await RazorGeneratedMap.GeneratorRanAsync(workspace, cancellationToken).ConfigureAwait(false);
        var health = ran
            ? "ok"
            : "unavailable - the Razor source generator produced nothing, so component types cannot be resolved; build the solution, or match its SDK to the terse version";

        return string.Create(CultureInfo.InvariantCulture, $"razor={razor} files generator={health}");
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

    private static string Render(WorkspaceLoadResult result, bool verbose)
    {
        var response = new ResponseBuilder("load_workspace", result.SolutionPath);

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"projects={result.ProjectCount} documents={result.DocumentCount} elapsedMs={result.ElapsedMilliseconds} failures={result.Failures.Count} warnings={result.Warnings.Count}"));

        AppendLoadDiagnostics(response, result, verbose);

        return response.ToString();
    }

    private static void AppendLoadDiagnostics(ResponseBuilder response, WorkspaceLoadResult result, bool verbose)
    {
        foreach (var failure in result.Failures)
            response.Line("FAILED " + failure);

        if (result.Warnings.Count is 0)
            return;

        if (!verbose)
        {
            response.Note(string.Create(
                CultureInfo.InvariantCulture,
                $"{result.Warnings.Count} MSBuild warning(s), not load failures; verbose=true lists them"));

            return;
        }

        foreach (var warning in result.Warnings)
            response.Line("WARNING " + warning);
    }

    private static string DescribeSync(WorkspaceSync sync) => string.Create(
        CultureInfo.InvariantCulture,
        $"watch={DescribeWatch(sync)} gen={sync.Generations} pending={sync.PendingCount} lastSyncMs={sync.LastSyncMilliseconds} gaps={sync.Gaps}");

    private static string DescribeWatch(WorkspaceSync sync) => sync.State switch
    {
        WatchState.Active => "active",
        WatchState.Off => "off",
        _ => "degraded(" + (sync.StateDetail ?? "unknown") + ")",
    };
}
