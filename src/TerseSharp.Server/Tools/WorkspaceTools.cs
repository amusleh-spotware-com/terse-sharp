using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class WorkspaceTools(ToolContext context)
{
    [McpServerTool(Name = "load_workspace")]
    [Description("Load a .sln/.slnx/.slnf/.csproj into memory. Call once per solution; every other tool needs it. Pass no path to auto-discover from the current directory, or discover=true to list what a directory contains without loading it. External edits are picked up automatically, so reload=true is only for a change the server cannot see. On a multi-targeted solution, targetFramework picks the framework every semantic tool answers from; loading the same solution under a different framework replaces the first. A load ends with compilations=cold when nothing is realized yet, and the first semantic call that realizes them reports how long that took, so the one-off cost is attributed to the call that paid it. It also warns when the PreToolUse guard or the skill is not installed.")]
    public Task<string> LoadWorkspace(
    [Description("Path to the solution or project. Empty = discover upwards from the working directory.")] string? path = null,
    [Description("Discard the in-memory solution and read it from disk again. Generation counters carry over and the undo history is cleared.")] bool reload = false,
    [Description("Target framework to evaluate a multi-targeted project as, e.g. net10.0. Empty lets MSBuild pick, and the answering framework stays implicit.")] string? targetFramework = null,
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

        var before = context.Served();

        var result = reload
            ? await context.Registry.ReloadAsync(target, cancellationToken).ConfigureAwait(false)
            : await context.Registry.LoadAsync(target, targetFramework, cancellationToken).ConfigureAwait(false);

        var rendered = AssetBanner.Appended(Render(context.Registry, result, verbose));

        await context.AnnounceAsync(before, cancellationToken).ConfigureAwait(false);

        return rendered;
    });

    [McpServerTool(Name = "list_workspaces", ReadOnly = true)]
    [Description("List loaded workspaces with their git branch and worktree, so you can disambiguate several checkouts of one repo. The solution path is absolute here, because it is what unload_workspace takes.")]
    public Task<string> ListWorkspaces() =>
    ToolBoundary.RunAsync(async () =>
    {
        await context.ReadyAsync().ConfigureAwait(false);

        var all = context.Registry.All();
        var response = new ResponseBuilder("list_workspaces", string.Empty);

        response.Summary(all.Count, all.Count, "workspaces");

        foreach (var workspace in all)
            response.Line(Describe(workspace, relative: false));

        if (all.Count is 0 && context.PreloadFailure is { } failure)
            response.Note("the startup preload failed: " + failure);

        return response.ToString();
    });

    [McpServerTool(Name = "unload_workspace", Destructive = true)]
    [Description("Unload a workspace and release its MSBuild file locks so an external build can run. Takes the solution path, not a worktree name - the absolute one list_workspaces prints, or the short one workspace_status and load_workspace print, which is resolved against every loaded root and refuses when two of them answer to it. Analyzer and source-generator assemblies are loaded from a shadow copy, so a project's own output is never mapped; any assembly that did end up mapped is still reported, because only restarting the server releases those.")]
    public Task<string> UnloadWorkspace(
[Description("Solution or project path to unload.")] string? path = null,
[Description("Alias for path; the solution path, not a worktree name.")] string? workspace = null,
CancellationToken cancellationToken = default) =>
(path ?? workspace) is { Length: > 0 } target
    ? ToolBoundary.RunAsync(async () =>
    {
        await context.ReadyAsync().ConfigureAwait(false);

        var resolved = Resolved(target);

        if (!resolved.IsOk)
            return resolved.Error!.Render();

        var solution = resolved.Value!;
        var mapped = Mapped(solution);
        var response = new ResponseBuilder("unload_workspace", target);
        var before = context.Served();

        response.Note(context.Registry.Unload(solution) ? "unloaded" : "not loaded");

        AppendMapped(response, mapped);

        await context.AnnounceAsync(before, cancellationToken).ConfigureAwait(false);

        return response.ToString();
    })
    : Task.FromResult(Errors.Blank("path").Render());

    private string[] Mapped(string target)
    {
        foreach (var loaded in context.Registry.All())
        {
            if (string.Equals(Path.GetFullPath(loaded.SolutionPath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                return MappedAnalyzers.Of(loaded.Solution);
        }

        return [];
    }

    private static void AppendMapped(ResponseBuilder response, string[] mapped)
    {
        if (mapped.Length is 0)
            return;

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"WARNING {mapped.Length} analyzer or source-generator assembly(ies) this workspace loaded are still mapped into this server process (pid {Environment.ProcessId}) and an external build that copies over them will fail MSB3027; only restarting the server releases them"));

        foreach (var assembly in mapped)
            response.Line(assembly);
    }

    [McpServerTool(Name = "workspace_status", ReadOnly = true)]
    [Description("Report a loaded workspace: solution, git worktree and branch, project and document counts, load time, any project that failed to load, and - when a tool profile or the loaded workspaces' own file kinds narrow the surface - which tools are advertised. It also warns, without verbose=true, when the PreToolUse guard or the skill is not installed, because an absent guard is what lets an agent answer with Read, Grep or dotnet build, and when a document's in-memory text no longer matches disk - the case where every other read answers from text that is gone. verbose=true adds the doctor self-checks and the in-sync count, so diagnosing terse needs no shell-out.")]
    public Task<string> WorkspaceStatus(
    [Description("Workspace or worktree name.")] string? workspace = null,
    [Description("List the MSBuild warnings the load reported, and the roslyn, assets, guard coverage and phases self-checks. Default false.")] bool verbose = false,
    CancellationToken cancellationToken = default) =>
    context.WithWorkspaceAsync(
        workspace,
        null,
        async loaded => AssetBanner.Appended(await RenderStatusAsync(loaded, verbose, context.Surface, context.Served(), cancellationToken).ConfigureAwait(false)),
        cancellationToken: cancellationToken);

    [McpServerTool(Name = "list_projects", ReadOnly = true)]
    [Description("List the projects of a loaded workspace: name, language, document count. The name is what build, run_tests, list_tests and clean accept as project=. path=<file> answers the opposite question - which project compiles that file, and whether an edit to it would be compile-gated. For a solution that is NOT loaded, call solution_projects path=<solution> instead - it answers from the file and loads nothing.")]
    public Task<string> ListProjects(
    [Description("Workspace or worktree name.")] string? workspace = null,
    [Description("Keep only projects whose name contains this text.")] string? filter = null,
    [Description("A file to answer about instead of listing: names the project that compiles it, or says no project does.")] string? path = null) =>
    context.WithWorkspace(workspace, path, loaded => path is { Length: > 0 } file ? RenderOwner(loaded, file) : RenderProjects(loaded, filter));

    private static string? Discover() =>
        WorkspaceDiscovery.Find(Directory.GetCurrentDirectory()) is [var first, ..] ? first : null;

    private static async Task<string> RenderStatusAsync(
            LoadedWorkspace workspace,
            bool verbose,
            ToolSurface surface,
            WorkspaceMarkup served,
            CancellationToken cancellationToken)
    {
        var response = new ResponseBuilder("workspace_status", workspace.SolutionPath).Verbose(verbose);
        var divergence = await WorkspaceDivergence.FindAsync(workspace, cancellationToken).ConfigureAwait(false);

        response.Note(Describe(workspace, relative: !verbose));
        response.Note(Counts(workspace, verbose));
        AppendSync(response, workspace.Sync, verbose);

        if (WorkspaceDivergence.Describe(divergence, verbose) is { } diverged)
            response.Note(diverged);

        await AppendRazorAsync(response, workspace, verbose, cancellationToken).ConfigureAwait(false);
        AppendMappedAnalyzers(response, workspace, verbose);

        if (ToolProfile.Describe(surface, served) is { } profile)
            response.Note(profile);

        if (AdvertisedCost.Describe(verbose) is { } cost)
            response.Note(cost);

        if (verbose)
            response.Note(workspace.Indexes.Describe());

        AppendLoadDiagnostics(response, workspace.Load, verbose);
        await AppendSelfChecksAsync(response, workspace, verbose, cancellationToken).ConfigureAwait(false);
        response.Note(Version());

        return response.ToString();
    }

    private static string Counts(LoadedWorkspace workspace, bool verbose)
    {
        var counts = verbose
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"documents={workspace.Load.DocumentCount} loadMs={workspace.Load.ElapsedMilliseconds} failures={workspace.Load.Failures.Count} warnings={workspace.Load.Warnings.Count}")
            : string.Create(CultureInfo.InvariantCulture, $"documents={workspace.Load.DocumentCount}");

        return workspace.TakeDroppedNotice()
            ? counts + string.Create(
                CultureInfo.InvariantCulture,
                $"  idle={(int)workspace.DroppedAfter.TotalMinutes}m compilations=dropped (this call re-realizes what it needs)")
            : counts;
    }

    private static void AppendSync(ResponseBuilder response, WorkspaceSync sync, bool verbose)
    {
        if (verbose || sync.State is not WatchState.Active || sync.Gaps > 0)
            response.Note(DescribeSync(sync));
    }

    private static async Task AppendRazorAsync(
        ResponseBuilder response,
        LoadedWorkspace workspace,
        bool verbose,
        CancellationToken cancellationToken)
    {
        var razor = workspace.Indexes.Razor().FileCount;

        if (razor is 0)
            return;

        var ran = await RazorGeneratedMap.GeneratorRanAsync(workspace, cancellationToken).ConfigureAwait(false);

        if (!ran)
        {
            response.Note(string.Create(
                CultureInfo.InvariantCulture,
                $"razor={razor} files generator=unavailable - the Razor source generator produced nothing, so component types cannot be resolved; build the solution, or match its SDK to the terse version"));

            return;
        }

        if (verbose)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"razor={razor} files generator=ok"));
    }

    private static string Describe(LoadedWorkspace workspace, bool relative) => string.Create(
    CultureInfo.InvariantCulture,
    $"{(relative ? PositionFormat.Relative(workspace.Root, workspace.SolutionPath) : workspace.SolutionPath)}  worktree={workspace.Git.WorktreeName} branch={workspace.Git.Branch}  projects={workspace.Load.ProjectCount}{Framework(workspace.Load.TargetFramework)}");

    private static string Framework(string? targetFramework) =>
        targetFramework is { Length: > 0 } chosen ? "  targetFramework=" + chosen : string.Empty;

    private static string Render(WorkspaceRegistry registry, WorkspaceLoadResult result, bool verbose)
    {
        var loaded = Find(registry, result.SolutionPath);
        var response = new ResponseBuilder("load_workspace", result.SolutionPath).Verbose(verbose);

        if (!verbose)
            response.Note(Shown(loaded, result.SolutionPath));

        response.Note(Loaded(result, verbose));
        AppendLoadDiagnostics(response, result, verbose);

        if (loaded is { CompilationsRealized: false })
            response.Note("compilations=cold - the first semantic call realizes them and pays for it once");

        return response.ToString();
    }

    private static string Loaded(WorkspaceLoadResult result, bool verbose) => verbose
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"projects={result.ProjectCount} documents={result.DocumentCount} elapsedMs={result.ElapsedMilliseconds} failures={result.Failures.Count} warnings={result.Warnings.Count}{Framework(result.TargetFramework)}")
        : string.Create(
            CultureInfo.InvariantCulture,
            $"projects={result.ProjectCount} documents={result.DocumentCount} failures={result.Failures.Count}{Framework(result.TargetFramework)}");

    private static void AppendLoadDiagnostics(ResponseBuilder response, WorkspaceLoadResult result, bool verbose)
    {
        var root = Path.GetDirectoryName(result.SolutionPath) ?? string.Empty;

        AppendFailures(response, result.Failures, root, verbose);
        AppendWarnings(response, result.Warnings, root, verbose);
    }

    private static void AppendFailures(ResponseBuilder response, IReadOnlyList<string> failures, string root, bool verbose)
    {
        if (failures.Count is 0)
            return;

        if (!verbose)
        {
            AppendFailedProjects(response, failures, root);

            return;
        }

        foreach (var failure in failures)
            response.Line("FAILED " + LoadFailureSummary.Relative(failure, root));
    }

    private static void AppendFailedProjects(ResponseBuilder response, IReadOnlyList<string> failures, string root)
    {
        var groups = LoadFailureSummary.Group(failures);
        var shown = Math.Min(groups.Length, MaxFailureGroups);

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"{failures.Count} load failure(s) in {groups.Length} project(s)"));

        for (var index = 0; index < shown; index++)
        {
            response.Line(string.Create(
                CultureInfo.InvariantCulture,
                $"FAILED {LoadFailureSummary.Relative(groups[index].Project, root)}  messages={groups[index].Count}"));
        }

        if (groups.Length > shown)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"{groups.Length - shown} more project(s) not listed"));
    }

    private static void AppendWarnings(ResponseBuilder response, IReadOnlyList<string> warnings, string root, bool verbose)
    {
        if (warnings.Count is 0)
            return;

        if (!verbose)
        {
            response.Note(string.Create(CultureInfo.InvariantCulture, $"{warnings.Count} MSBuild warning(s), not load failures"));

            return;
        }

        foreach (var warning in warnings)
            response.Line("WARNING " + LoadFailureSummary.Relative(warning, root));
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

    private static string RenderProjects(LoadedWorkspace workspace, string? filter)
    {
        var projects = workspace.Solution.Projects
            .Where(project => Matches(project.Name, filter))
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ToArray();
        var response = new ResponseBuilder("list_projects", workspace.SolutionPath);
        response.Summary(projects.Length, projects.Length, "projects", "filter=");

        foreach (var project in projects)
        {
            response.Line(string.Create(
                CultureInfo.InvariantCulture,
                $"{project.Name}  {project.Language}  documents={project.Documents.Count()}  {PositionFormat.Relative(workspace.Root, project.FilePath)}"));
        }

        return response.ToString();
    }

    private static bool Matches(string name, string? filter) =>
        filter is not { Length: > 0 } || name.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private const int MaxFailureGroups = 20;

    private static void AppendMappedAnalyzers(ResponseBuilder response, LoadedWorkspace workspace, bool verbose)
    {
        var mapped = MappedAnalyzers.Of(workspace.Solution);

        if (mapped.Length is 0 && !verbose)
            return;

        response.Note(MappedNote(mapped.Length));

        if (!verbose)
            return;

        foreach (var assembly in mapped)
            response.Line(assembly);
    }

    private static string MappedNote(int count) => count is 0
        ? "mapped=0"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"mapped={count} analyzer or source-generator assembly(ies) are held by this server process (pid {Environment.ProcessId}); an external build that copies over them fails MSB3027, and only restarting the server releases them");

    private static LoadedWorkspace? Find(WorkspaceRegistry registry, string solutionPath)
    {
        foreach (var loaded in registry.All())
        {
            if (Path.GetFullPath(loaded.SolutionPath).Equals(Path.GetFullPath(solutionPath), StringComparison.OrdinalIgnoreCase))
                return loaded;
        }

        return null;
    }

    private static string Shown(LoadedWorkspace? loaded, string solutionPath) =>
        loaded is null ? solutionPath : PositionFormat.Relative(loaded.Root, solutionPath);

    private Result<string> Resolved(string target)
    {
        var full = Path.GetFullPath(target);
        var matched = new List<string>();

        foreach (var loaded in context.Registry.All())
        {
            if (Path.GetFullPath(loaded.SolutionPath).Equals(full, StringComparison.OrdinalIgnoreCase)
                || Shown(loaded, loaded.SolutionPath).Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                matched.Add(loaded.SolutionPath);
            }
        }

        return matched switch
        {
            [var only] => Result.Ok(only),
            [] => Result.Ok(target),
            _ => Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{target}' names {matched.Count} loaded workspaces"),
                "pass the absolute solution path; list_workspaces prints it in full for exactly this reason")),
        };
    }

    private static string Version()
    {
        var version = UpdateSettings.Version();

        return version.Length is 0 ? "terse=unknown" : "terse=" + version;
    }

    private static async Task AppendSelfChecksAsync(ResponseBuilder response, LoadedWorkspace workspace, bool verbose, CancellationToken cancellationToken)
    {
        if (!verbose)
            return;

        foreach (var check in await Doctor.SelfChecksAsync(workspace, cancellationToken).ConfigureAwait(false))
            response.Note(check);
    }

    private static string RenderOwner(LoadedWorkspace workspace, string path)
    {
        var response = new ResponseBuilder("list_projects", path);
        var resolved = PathGuard.Resolve(workspace, path);

        if (!resolved.IsOk)
            return resolved.Error!.Render();

        if (DocumentLookup.Find(workspace, path) is { } document)
        {
            response.Summary(1, 1, "projects");
            response.Line(Owner(workspace, document.Project, "compiles it - an edit is compile-gated"));

            return response.ToString();
        }

        if (FileService.CompilingProject(workspace, resolved.Value!) is { } globbing)
        {
            response.Summary(1, 1, "projects");
            response.Line(Owner(workspace, globbing, "would compile it once written - a new file there is compile-gated"));

            return response.ToString();
        }

        response.Summary(0, 0, "projects");
        response.Note("no project of this solution compiles that path, so an edit to it is not compile-gated");

        return response.ToString();
    }

    private static string Owner(LoadedWorkspace workspace, Microsoft.CodeAnalysis.Project project, string verdict) => string.Create(
        CultureInfo.InvariantCulture,
        $"{project.Name}  {PositionFormat.Relative(workspace.Root, project.FilePath)}  {verdict}");
}
