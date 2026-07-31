using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace TerseSharp.Core;

public sealed class LoadedWorkspace : IDisposable
{
    private const int HistoryDepth = 10;

    private readonly MSBuildWorkspace workspace;
    private readonly List<Solution> history = [];
    private readonly Lock historyGate = new();
    private readonly Lock leaseGate = new();

    private int leases;
    private bool retired;

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

    public WorkspaceLease Lease()
    {
        lock (leaseGate)
            leases++;

        return new WorkspaceLease(this);
    }

    public bool TryApply(Solution solution)
    {
        lock (historyGate)
        {
            var previous = workspace.CurrentSolution;

            if (!workspace.TryApplyChanges(solution))
                return false;

            history.Add(previous);

            if (history.Count > HistoryDepth)
                history.RemoveAt(0);

            return true;
        }
    }

    public string Undo()
    {
        lock (historyGate)
        {
            if (history.Count is 0)
                return "nothing to undo";

            var previous = history[^1];

            history.RemoveAt(history.Count - 1);

            return workspace.TryApplyChanges(previous)
                ? "reverted the last change"
                : "the workspace refused the revert";
        }
    }

    public void Dispose()
    {
        if (Retire())
            workspace.Dispose();
    }

    internal void Release()
    {
        if (Returned())
            workspace.Dispose();
    }

    private bool Retire()
    {
        lock (leaseGate)
        {
            if (retired)
                return false;

            retired = true;

            return leases is 0;
        }
    }

    private bool Returned()
    {
        lock (leaseGate)
        {
            leases--;

            return retired && leases is 0;
        }
    }
}
