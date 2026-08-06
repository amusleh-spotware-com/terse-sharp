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
    private readonly Lazy<string> lineEnding;

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
        LoadedUtc = DateTimeOffset.UtcNow;
        Solution = Forked();
        dropped = seed.UndoNote;
        Sync = new WorkspaceSync(Root, seed.Generations);
        Indexes = new WorkspaceIndexes(Root, Sync);
        watcher = WorkspaceWatcher.Create(Root, Sync, seed.Watch);
        lineEnding = new Lazy<string>(() => DetectLineEnding(SourceSample() ?? load.SolutionPath));
    }

    public WorkspaceLoadResult Load { get; }

    public GitContext Git { get; }

    public string Root { get; }

    public WorkspaceSync Sync { get; }

    public WorkspaceIndexes Indexes { get; }

    public string SolutionPath => Load.SolutionPath;

    public string LineEnding => lineEnding.Value;

    public DateTimeOffset LastUsedUtc { get; private set; }

    public DateTimeOffset LoadedUtc { get; }

    public Solution Solution { get; private set; }

    public void Touch()
    {
        LastUsedUtc = DateTimeOffset.UtcNow;
        CompilationsDropped = false;
    }

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
        var before = workspace.CurrentSolution;
        var entry = await CaptureAsync(before, changed, cancellationToken).ConfigureAwait(false);
        var rebased = await RebasedAsync(solution, changed, cancellationToken).ConfigureAwait(false);

        if (!Committed(rebased, solution, entry))
            return false;

        foreach (var path in entry.Paths)
            Sync.Settled(path);

        Sync.Bumped(ChangeKind.Code);

        if (Moved(before, Solution, changed))
            Sync.Bumped(ChangeKind.Files);

        return true;
    }

    private static bool Moved(Solution before, Solution after, IReadOnlyList<DocumentId> changed)
    {
        foreach (var document in changed)
        {
            if (before.GetDocument(document) is null || after.GetDocument(document) is null)
                return true;
        }

        return false;
    }

    public async Task<bool> AdoptAsync(Solution solution, CancellationToken cancellationToken)
    {
        var rebased = await AbsorbedAsync(solution, cancellationToken).ConfigureAwait(false);

        lock (historyGate)
        {
            if (!Applied(rebased))
                return false;

            Solution = solution;

            return true;
        }
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

            return Reverted(entry);
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

    private Solution Forked() => AnalyzerRebind.Rebound(workspace.CurrentSolution, ShadowCopyAnalyzerLoader.Shared);

    private bool Committed(Solution rebased, Solution forked, HistoryEntry entry)
    {
        lock (historyGate)
        {
            if (!Applied(rebased))
                return false;

            Record(entry);

            Solution = forked;

            return true;
        }
    }

    private string Reverted(HistoryEntry entry)
    {
        var target = workspace.CurrentSolution;
        var restored = Solution;

        foreach (var revision in entry.Documents)
        {
            target = Restored(target, revision);
            restored = Restored(restored, revision);
        }

        if (!Applied(target))
            return "the workspace refused the revert";

        Solution = restored;

        return "reverted the last change";
    }

    private static Solution Restored(Solution solution, DocumentRevision revision) =>
        solution.ContainsDocument(revision.Id)
            ? solution.WithDocumentText(revision.Id, revision.Text)
            : solution.AddDocument(
                revision.Id,
                revision.Name,
                revision.Text,
                revision.Folders,
                revision.FilePath);

    private async Task<Solution> AbsorbedAsync(Solution solution, CancellationToken cancellationToken)
    {
        var target = workspace.CurrentSolution;

        foreach (var change in solution.GetChanges(Solution).GetProjectChanges())
            target = await ProjectedAsync(solution, target, change, cancellationToken).ConfigureAwait(false);

        return target;
    }

    private static async Task<Solution> ProjectedAsync(
        Solution source,
        Solution target,
        ProjectChanges change,
        CancellationToken cancellationToken)
    {
        var projected = target.RemoveDocuments([.. change.GetRemovedDocuments()]);

        foreach (var id in change.GetAddedDocuments())
            projected = await AddedAsync(source, projected, id, cancellationToken).ConfigureAwait(false);

        foreach (var id in change.GetChangedDocuments())
            projected = await ChangedAsync(source, projected, id, cancellationToken).ConfigureAwait(false);

        foreach (var id in change.GetChangedAdditionalDocuments())
            projected = await AdditionalAsync(source, projected, id, cancellationToken).ConfigureAwait(false);

        return projected;
    }

    private static async Task<Solution> AddedAsync(
        Solution source,
        Solution target,
        DocumentId id,
        CancellationToken cancellationToken)
    {
        if (source.GetDocument(id) is not { } document)
            return target;

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);

        return target.AddDocument(id, document.Name, text, document.Folders, document.FilePath);
    }

    private static async Task<Solution> ChangedAsync(
        Solution source,
        Solution target,
        DocumentId id,
        CancellationToken cancellationToken)
    {
        if (source.GetDocument(id) is not { } document || target.GetDocument(id) is null)
            return target;

        return target.WithDocumentText(id, await document.GetTextAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task<Solution> AdditionalAsync(
        Solution source,
        Solution target,
        DocumentId id,
        CancellationToken cancellationToken)
    {
        if (source.GetAdditionalDocument(id) is not { } document || target.GetAdditionalDocument(id) is null)
            return target;

        return target.WithAdditionalDocumentText(id, await document.GetTextAsync(cancellationToken).ConfigureAwait(false));
    }

    private void Shutdown()
    {
        RazorGeneratedMap.Forget(Solution.ProjectIds);
        workspace.Dispose();
        Solution = workspace.CurrentSolution;
        Sync.Dispose();
        Indexes.Dispose();
    }
    private static string DetectLineEnding(string solutionPath)
    {
        try
        {
            return LineEndings.Dominant(File.ReadAllText(solutionPath));
        }
        catch (IOException)
        {
            return Environment.NewLine;
        }
        catch (UnauthorizedAccessException)
        {
            return Environment.NewLine;
        }
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

            revisions.Add(new DocumentRevision(
                id,
                await document.GetTextAsync(cancellationToken).ConfigureAwait(false),
                document.Name,
                [.. document.Folders],
                path));
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

    private readonly record struct DocumentRevision(
        DocumentId Id,
        SourceText Text,
        string Name,
        IReadOnlyList<string> Folders,
        string FilePath);

    private sealed record HistoryEntry(IReadOnlyList<DocumentRevision> Documents, string[] Paths)
    {
        public bool Covers(IReadOnlyList<string> paths) =>
            Paths.Any(path => paths.Contains(path, StringComparer.OrdinalIgnoreCase));
    }

    private string? SourceSample() => Solution
            .Projects
            .SelectMany(project => project.Documents)
            .Select(document => document.FilePath)
            .FirstOrDefault(file => file is { Length: > 0 } && SourceFile.IsCSharp(file) && !GeneratedCode.IsGenerated(Root, file));

    private async Task<Solution> RebasedAsync(
        Solution solution,
        IReadOnlyList<DocumentId> changed,
        CancellationToken cancellationToken)
    {
        var target = workspace.CurrentSolution;

        foreach (var id in changed)
            target = await ProjectedAsync(solution, target, id, cancellationToken).ConfigureAwait(false);

        return target;
    }

    private static Task<Solution> ProjectedAsync(
        Solution source,
        Solution target,
        DocumentId id,
        CancellationToken cancellationToken) =>
        target.GetDocument(id) is null
            ? AddedAsync(source, target, id, cancellationToken)
            : ChangedAsync(source, target, id, cancellationToken);

    private bool Applied(Solution rebased)
    {
        var applied = false;

        try
        {
            applied = workspace.TryApplyChanges(rebased);
        }
        finally
        {
            if (!applied)
                Solution = Forked();
        }

        return applied;
    }

    public bool CompilationsDropped { get; private set; }

    public TimeSpan Idle => DateTimeOffset.UtcNow - LastUsedUtc;

    public bool DropCompilations()
    {
        lock (leaseGate)
        {
            if (leases is not 0 || retired)
                return false;
        }

        lock (historyGate)
        {
            Solution = Forked();
            CompilationsDropped = true;
        }

        return true;
    }
}
