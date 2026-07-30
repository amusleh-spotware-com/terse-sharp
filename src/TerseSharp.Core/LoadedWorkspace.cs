using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace TerseSharp.Core;

public sealed class LoadedWorkspace : IDisposable
{
    private readonly MSBuildWorkspace workspace;

    internal LoadedWorkspace(MSBuildWorkspace workspace, WorkspaceLoadResult load, GitContext git)
    {
        this.workspace = workspace;
        Load = load;
        Git = git;
        Root = Path.GetDirectoryName(Path.GetFullPath(load.SolutionPath)) ?? load.SolutionPath;
        LastUsedUtc = DateTimeOffset.UtcNow;
    }

    public WorkspaceLoadResult Load { get; }

    public GitContext Git { get; }

    public string Root { get; }

    public string SolutionPath => Load.SolutionPath;

    public DateTimeOffset LastUsedUtc { get; private set; }

    public Solution Solution => workspace.CurrentSolution;

    public void Touch() => LastUsedUtc = DateTimeOffset.UtcNow;

    public bool Contains(string path) => PathBoundary.Contains(Root, path);

    public bool TryApply(Solution solution) => workspace.TryApplyChanges(solution);

    public void Dispose() => workspace.Dispose();
}
