using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class ProjectTools(ToolContext context)
{
    [McpServerTool(Name = "solution_projects", ReadOnly = true)]
    [Description("List the project paths recorded in the solution file itself (.slnx or .sln), as opposed to what is currently loaded. path= reads a solution that is NOT loaded - a fixture, a sibling repository - so 'which projects does this solution contain' costs one call instead of a load_workspace that then makes every un-hinted call ambiguous; a relative path= is resolved against the server's working directory, not a workspace, so the answer names the file it actually read. A .slnf solution filter is JSON and is not parsed yet: it is refused, never answered as 0 projects.")]
    public Task<string> SolutionProjects(
    [Description("Workspace or worktree name.")] string? workspace = null,
    [Description("Path to a .slnx or .sln to read directly, loaded or not. Absolute is unambiguous; a relative path is resolved against the server's working directory. Empty reads the solution of the resolved workspace.")] string? path = null,
    CancellationToken cancellationToken = default) =>
    path is { Length: > 0 }
        ? Unloaded(path, cancellationToken)
        : context.WithWorkspaceAsync(
            workspace,
            null,
            async loaded => NavigationTools.Unwrap(await SolutionFile.RenderAsync(loaded.SolutionPath, echoPath: false, cancellationToken).ConfigureAwait(false)),
            semantic: false,
            cancellationToken);

    private static async Task<string> Unloaded(string path, CancellationToken cancellationToken) =>
    NavigationTools.Unwrap(await SolutionFile
        .RenderAsync(Path.GetFullPath(path), echoPath: true, cancellationToken)
        .ConfigureAwait(false));

    [McpServerTool(Name = "solution_add_project")]
    [Description("Add an existing project to the .slnx solution, preserving the rest of the file. A successful edit answers in one line; pass verbose=true for the diff.")]
    public Task<string> SolutionAddProject(
    [Description("Path to the .csproj to add.")] string project,
    [Description("Diff only, write nothing.")] bool dryRun = false,
    [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
    [Description("Workspace or worktree name.")] string? workspace = null,
    CancellationToken cancellationToken = default) =>
    GuardedSolution(workspace, project, dryRun, loaded => SolutionFile.AddProject(loaded.SolutionPath, project, dryRun, verbose, cancellationToken));

    [McpServerTool(Name = "solution_remove_project", Destructive = true)]
    [Description("Remove a project from the .slnx solution without deleting it from disk. A successful edit answers in one line; pass verbose=true for the diff.")]
    public Task<string> SolutionRemoveProject(
    [Description("Path to the .csproj to remove.")] string project,
    [Description("Diff only, write nothing.")] bool dryRun = false,
    [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
    [Description("Workspace or worktree name.")] string? workspace = null,
    CancellationToken cancellationToken = default) =>
    GuardedSolution(workspace, project, dryRun, loaded => SolutionFile.RemoveProject(loaded.SolutionPath, project, dryRun, verbose, cancellationToken));

    [McpServerTool(Name = "project_create")]
    [Description("Create a new SDK-style .csproj. Use solution_add_project afterwards to put it in the solution.")]
    public Task<string> ProjectCreate(
        [Description("Path of the .csproj to create.")] string project,
        [Description("classlib, console, web or razor. Default classlib.")] string? kind = null,
        [Description("Optional target framework, e.g. net10.0. Omit to inherit from Directory.Build.props.")] string? targetFramework = null,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Guarded(workspace, project, dryRun, loaded =>
            ProjectFile.Create(Resolve(loaded, project), kind ?? "classlib", targetFramework, dryRun, verbose));

    [McpServerTool(Name = "project_properties", ReadOnly = true)]
    [Description("Read a project's MSBuild properties as MSBuild itself evaluated them - the winning value of each, with the file that set it - so a property a Directory.Build.props declares is answered, not the empty list the project file's own XML would give. Properties defined outside the workspace root, which is the SDK's own hundreds, are left out.")]
    public Task<string> ProjectProperties(
        [Description("Path to the .csproj.")] string project,
        [Description("Optional property name to filter by.")] string? name = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        ReadingAsync(workspace, project, loaded => ProjectEvaluation.Properties(loaded.Root, Resolve(loaded, project), name, cancellationToken));

    [McpServerTool(Name = "project_set_property")]
    [Description("Set or add an MSBuild property in a project file, preserving the rest of the XML.")]
    public Task<string> ProjectSetProperty(
        [Description("Path to the .csproj.")] string project,
        [Description("Property name, e.g. LangVersion.")] string name,
        [Description("Property value.")] string value,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Guarded(workspace, project, dryRun, loaded => ProjectFile.SetProperty(Resolve(loaded, project), name, value, dryRun, verbose));

    [McpServerTool(Name = "project_add_reference")]
    [Description("Add a ProjectReference from one project to another.")]
    public Task<string> ProjectAddReference(
        [Description("Path to the .csproj to modify.")] string project,
        [Description("Path to the .csproj to reference.")] string target,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Guarded(workspace, project, dryRun, loaded => ProjectFile.AddReference(Resolve(loaded, project), Resolve(loaded, target), dryRun, verbose));

    [McpServerTool(Name = "project_remove_reference", Destructive = true)]
    [Description("Remove a ProjectReference from a project.")]
    public Task<string> ProjectRemoveReference(
        [Description("Path to the .csproj to modify.")] string project,
        [Description("Path to the referenced .csproj.")] string target,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Guarded(workspace, project, dryRun, loaded => ProjectFile.RemoveReference(Resolve(loaded, project), Resolve(loaded, target), dryRun, verbose));

    [McpServerTool(Name = "package_list", ReadOnly = true)]
    [Description("Replaces Bash dotnet list package. Lists the package and project references a project declares; vulnerable=true or outdated=true answers from the restored graph instead, which is the question the project file cannot answer - a known advisory or a newer version - so the last dependency question needs no shell. The two are mutually exclusive, exactly as the CLI's own flags are.")]
    public Task<string> PackageList(
        [Description("Path to the .csproj.")] string project,
        [Description("Report packages with a known security advisory, read from the restored graph including transitive ones. Needs a restore. Cannot be combined with outdated. Default false.")] bool vulnerable = false,
        [Description("Report packages with a newer version available on the configured feeds. Needs a restore and network access. Cannot be combined with vulnerable. Default false.")] bool outdated = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default)
    {
        if (vulnerable && outdated)
        {
            return Task.FromResult(Errors.Invalid(
                "vulnerable and outdated are mutually exclusive, because dotnet list package refuses both flags together",
                "call package_list vulnerable=true and package_list outdated=true separately").Render());
        }

        return vulnerable || outdated
            ? context.WithTargetAsync(workspace, project, target => DotnetRunner.AuditPackagesAsync(
                target.Root, Path.IsPathRooted(project) ? project : Path.Combine(target.Root, project), vulnerable, cancellationToken), cancellationToken: cancellationToken)
            : Reading(workspace, project, loaded => ProjectFile.ListPackages(Resolve(loaded, project)));
    }

    [McpServerTool(Name = "package_add")]
    [Description("Add a PackageReference. Central Package Management aware: with Directory.Packages.props the version is written there and the reference stays version-less.")]
    public Task<string> PackageAdd(
        [Description("Path to the .csproj.")] string project,
        [Description("Package id, e.g. Serilog.")] string package,
        [Description("Package version. Required when the solution uses central package management.")] string? version = null,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Guarded(workspace, project, dryRun, loaded => ProjectFile.AddPackage(loaded.Root, Resolve(loaded, project), package, version, dryRun, verbose));

    [McpServerTool(Name = "package_remove", Destructive = true)]
    [Description("Remove a PackageReference from a project.")]
    public Task<string> PackageRemove(
        [Description("Path to the .csproj.")] string project,
        [Description("Package id to remove.")] string package,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Guarded(workspace, project, dryRun, loaded => ProjectFile.RemovePackage(Resolve(loaded, project), package, dryRun, verbose));

    private static string Resolve(LoadedWorkspace workspace, string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(workspace.Root, path);

    private Task<string> Guarded(
        string? workspace,
        string path,
        bool dryRun,
        Func<LoadedWorkspace, string> written,
        Func<LoadedWorkspace, Task<Result<string>>> action)
    {
        if (context.RejectWrite() is { } rejection)
            return Task.FromResult(rejection);

        return ReadingAsync(workspace, path, async loaded =>
        {
            var result = await action(loaded).ConfigureAwait(false);

            if (result.IsOk && !dryRun)
                loaded.Sync.Noticed(written(loaded), ChangeKind.Project);

            return result;
        });
    }

    private Task<string> ReadingAsync(string? workspace, string path, Func<LoadedWorkspace, Task<Result<string>>> action)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult(Errors.Blank("path").Render());

        return context.WithWorkspaceAsync(workspace, path, async loaded =>
        {
            var guard = PathGuard.Resolve(loaded, path);

            return guard.IsOk ? NavigationTools.Unwrap(await action(loaded).ConfigureAwait(false)) : guard.Error!.Render();
        });
    }

    private Task<string> Reading(string? workspace, string path, Func<LoadedWorkspace, Result<string>> action)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult(Errors.Blank("path").Render());

        return context.WithWorkspace(workspace, path, loaded =>
        {
            var guard = PathGuard.Resolve(loaded, path);

            return guard.IsOk ? NavigationTools.Unwrap(action(loaded)) : guard.Error!.Render();
        });
    }

    private Task<string> Guarded(string? workspace, string path, bool dryRun, Func<LoadedWorkspace, Task<Result<string>>> action) =>
        Guarded(workspace, path, dryRun, loaded => Resolve(loaded, path), action);

    private Task<string> GuardedSolution(string? workspace, string path, bool dryRun, Func<LoadedWorkspace, Task<Result<string>>> action) =>
        Guarded(workspace, path, dryRun, loaded => loaded.SolutionPath, action);
}
