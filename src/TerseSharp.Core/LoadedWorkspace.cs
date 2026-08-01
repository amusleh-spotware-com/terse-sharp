using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

public sealed class LoadedWorkspace : IDisposable
{
    private const int HistoryDepth = 10;

    private readonly MSBuildWorkspace workspace;
    private readonly List<HistoryEntry> history = [];
    private readonly Lock historyGate = new();
    private readonly Lock leaseGate = new();
    private readonly WorkspaceWatcher watcher;

    private int leases;
    private bool retired;
    private string? dropped;

    internal LoadedWorkspace(MSBuildWorkspace workspace, WorkspaceLoadResult load, GitContext git, WorkspaceSeed seed)
    {
        this.workspace = workspace;
        Load = load;
        Git = git;
        Root = Path.GetDirectoryName(Path.GetFullPath(load.SolutionPath)) ?? load.SolutionPath;
        LastUsedUtc = DateTimeOffset.UtcNow;
        dropped = seed.UndoNote;
        Sync = new WorkspaceSync(Root, seed.Generations);
        watcher = WorkspaceWatcher.Create(Root, Sync, seed.Watch);
    }

    public WorkspaceLoadResult Load { get; }

    public GitContext Git { get; }

    public string Root { get; }

    public WorkspaceSync Sync { get; }

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

    public async Task<bool> TryApplyAsync(
        Solution solution,
        IReadOnlyList<DocumentId> changed,
        CancellationToken cancellationToken)
    {
        var entry = await CaptureAsync(workspace.CurrentSolution, changed, cancellationToken).ConfigureAwait(false);

        lock (historyGate)
        {
            if (!workspace.TryApplyChanges(solution))
                return false;

            Record(entry);
        }

        foreach (var path in entry.Paths)
            Sync.Settled(path);

        return true;
    }

    public bool Adopt(Solution solution)
    {
        lock (historyGate)
            return workspace.TryApplyChanges(solution);
    }

    public void DropSnapshots(IReadOnlyList<string> paths)
    {
        if (paths.Count is 0)
            return;

        lock (historyGate)
            Discard(paths);
    }

    public string Undo()
    {
        lock (historyGate)
        {
            if (history.Count is 0)
                return dropped is null ? "nothing to undo" : "nothing to undo - " + dropped;

            var entry = history[^1];

            history.RemoveAt(history.Count - 1);

            return workspace.TryApplyChanges(Restore(entry))
                ? "reverted the last change"
                : "the workspace refused the revert";
        }
    }

    public void Dispose()
    {
        watcher.Dispose();

        if (Retire())
            Shutdown();
    }

    internal string? UndoNote()
    {
        lock (historyGate)
            return history.Count is 0 ? dropped : Describe(history.Count, "the workspace reloaded");
    }

    internal void Release()
    {
        if (Returned())
            Shutdown();
    }

    private void Shutdown()
    {
        workspace.Dispose();
        Sync.Dispose();
    }

    private Solution Restore(HistoryEntry entry)
    {
        var solution = workspace.CurrentSolution;

        foreach (var revision in entry.Documents)
            solution = solution.WithDocumentText(revision.Id, revision.Text);

        return solution;
    }

    private static async Task<HistoryEntry> CaptureAsync(
        Solution solution,
        IReadOnlyList<DocumentId> changed,
        CancellationToken cancellationToken)
    {
        var revisions = new List<DocumentRevision>(changed.Count);
        var paths = new List<string>(changed.Count);

        foreach (var id in changed)
        {
            if (solution.GetDocument(id) is not { FilePath: { } path } document)
                continue;

            revisions.Add(new DocumentRevision(id, await document.GetTextAsync(cancellationToken).ConfigureAwait(false)));
            paths.Add(path);
        }

        return new HistoryEntry(revisions, [.. paths]);
    }

    private void Record(HistoryEntry entry)
    {
        dropped = null;
        history.Add(entry);

        if (history.Count > HistoryDepth)
            history.RemoveAt(0);
    }

    private void Discard(IReadOnlyList<string> paths)
    {
        var index = history.FindIndex(entry => entry.Covers(paths));

        if (index < 0)
            return;

        dropped = Describe(history.Count - index, "an external change to " + PositionFormat.Relative(Root, paths[0]));
        history.RemoveRange(index, history.Count - index);
    }

    private static string Describe(int count, string cause) => string.Create(
        CultureInfo.InvariantCulture,
        $"{count} snapshot(s) were dropped after {cause}");

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

    private readonly record struct DocumentRevision(DocumentId Id, SourceText Text);

    private sealed record HistoryEntry(IReadOnlyList<DocumentRevision> Documents, string[] Paths)
    {
        public bool Covers(IReadOnlyList<string> paths) =>
            Paths.Any(path => paths.Contains(path, StringComparer.OrdinalIgnoreCase));
    }
}
