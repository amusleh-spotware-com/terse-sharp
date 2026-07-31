using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class ProjectTools(ToolContext context)
{
    [McpServerTool(Name = "solution_projects")]
    [Description("List the project paths recorded in the solution file itself (.slnx, .sln or .slnf), as opposed to what is currently loaded.")]
    public Task<string> SolutionProjects([Description("Workspace or worktree name.")] string? workspace = null) =>
        context.WithWorkspace(workspace, null, loaded =>
        {
            var projects = SolutionFile.Projects(loaded.SolutionPath);
            var response = new ResponseBuilder("solution_projects", loaded.SolutionPath);

            response.Summary(projects.Count, projects.Count, "projects");

            foreach (var project in projects)
                response.Line(project);

            return response.ToString();
        });

    [McpServerTool(Name = "solution_add_project")]
    [Description("Add an existing project to the .slnx solution, preserving the rest of the file. Returns the diff.")]
    public Task<string> SolutionAddProject(
        [Description("Path to the .csproj to add.")] string project,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Guarded(workspace, project, loaded => SolutionFile.AddProject(loaded.SolutionPath, project, dryRun));

    [McpServerTool(Name = "solution_remove_project")]
    [Description("Remove a project from the .slnx solution without deleting it from disk. Returns the diff.")]
    public Task<string> SolutionRemoveProject(
        [Description("Path to the .csproj to remove.")] string project,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Guarded(workspace, project, loaded => SolutionFile.RemoveProject(loaded.SolutionPath, project, dryRun));

    [McpServerTool(Name = "project_create")]
    [Description("Create a new SDK-style .csproj. Use solution_add_project afterwards to put it in the solution.")]
    public Task<string> ProjectCreate(
        [Description("Path of the .csproj to create.")] string project,
        [Description("classlib, console, web or razor. Default classlib.")] string? kind = null,
        [Description("Optional target framework, e.g. net10.0. Omit to inherit from Directory.Build.props.")] string? targetFramework = null,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Guarded(workspace, project, loaded =>
            ProjectFile.Create(Resolve(loaded, project), kind ?? "classlib", targetFramework, dryRun));

    [McpServerTool(Name = "project_properties")]
    [Description("Read the MSBuild properties declared in a project file.")]
    public Task<string> ProjectProperties(
        [Description("Path to the .csproj.")] string project,
        [Description("Optional property name to filter by.")] string? name = null,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Reading(workspace, project, loaded => ProjectFile.GetProperties(Resolve(loaded, project), name));

    [McpServerTool(Name = "project_set_property")]
    [Description("Set or add an MSBuild property in a project file, preserving the rest of the XML.")]
    public Task<string> ProjectSetProperty(
        [Description("Path to the .csproj.")] string project,
        [Description("Property name, e.g. LangVersion.")] string name,
        [Description("Property value.")] string value,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Guarded(workspace, project, loaded => ProjectFile.SetProperty(Resolve(loaded, project), name, value, dryRun));

    [McpServerTool(Name = "project_add_reference")]
    [Description("Add a ProjectReference from one project to another.")]
    public Task<string> ProjectAddReference(
        [Description("Path to the .csproj to modify.")] string project,
        [Description("Path to the .csproj to reference.")] string target,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Guarded(workspace, project, loaded => ProjectFile.AddReference(Resolve(loaded, project), Resolve(loaded, target), dryRun));

    [McpServerTool(Name = "project_remove_reference")]
    [Description("Remove a ProjectReference from a project.")]
    public Task<string> ProjectRemoveReference(
        [Description("Path to the .csproj to modify.")] string project,
        [Description("Path to the referenced .csproj.")] string target,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Guarded(workspace, project, loaded => ProjectFile.RemoveReference(Resolve(loaded, project), Resolve(loaded, target), dryRun));

    [McpServerTool(Name = "package_list")]
    [Description("List the package and project references declared in a project file.")]
    public Task<string> PackageList(
        [Description("Path to the .csproj.")] string project,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Reading(workspace, project, loaded => ProjectFile.ListPackages(Resolve(loaded, project)));

    [McpServerTool(Name = "package_add")]
    [Description("Add a PackageReference. Central Package Management aware: with Directory.Packages.props the version is written there and the reference stays version-less.")]
    public Task<string> PackageAdd(
        [Description("Path to the .csproj.")] string project,
        [Description("Package id, e.g. Serilog.")] string package,
        [Description("Package version. Required when the solution uses central package management.")] string? version = null,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Guarded(workspace, project, loaded => ProjectFile.AddPackage(loaded.Root, Resolve(loaded, project), package, version, dryRun));

    [McpServerTool(Name = "package_remove")]
    [Description("Remove a PackageReference from a project.")]
    public Task<string> PackageRemove(
        [Description("Path to the .csproj.")] string project,
        [Description("Package id to remove.")] string package,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Workspace or worktree name.")] string? workspace = null) =>
        Guarded(workspace, project, loaded => ProjectFile.RemovePackage(Resolve(loaded, project), package, dryRun));

    private static string Resolve(LoadedWorkspace workspace, string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(workspace.Root, path);

    private Task<string> Guarded(string? workspace, string path, Func<LoadedWorkspace, Result<string>> action)
    {
        if (context.RejectWrite() is { } rejection)
            return Task.FromResult(rejection);

        return Reading(workspace, path, action);
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
}
