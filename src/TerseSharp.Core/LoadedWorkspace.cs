using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace TerseSharp.Core;

public sealed class LoadedWorkspace : IDisposable
{
    private const int HistoryDepth = 10;

    private readonly MSBuildWorkspace workspace;
    private readonly List<Solution> history = [];
    private readonly Lock historyGate = new();

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

    public bool TryApply(Solution solution)
    {
        var previous = workspace.CurrentSolution;

        if (!workspace.TryApplyChanges(solution))
            return false;

        Remember(previous);

        return true;
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

    private void Remember(Solution previous)
    {
        lock (historyGate)
        {
            history.Add(previous);

            if (history.Count > HistoryDepth)
                history.RemoveAt(0);
        }
    }

    public void Dispose() => workspace.Dispose();
}
