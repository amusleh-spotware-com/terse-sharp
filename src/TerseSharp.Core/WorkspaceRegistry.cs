namespace TerseSharp.Core;

public sealed class WorkspaceRegistry(int maxWorkspaces = 4, bool watch = true) : IDisposable
{
    private readonly Dictionary<string, LoadedWorkspace> workspaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock map = new();
    private readonly int maxWorkspaces = maxWorkspaces;
    private readonly bool watch = watch;

    public Task<WorkspaceLoadResult> LoadAsync(string path, CancellationToken cancellationToken) =>
        LoadAsync(path, null, cancellationToken);

    public async Task<WorkspaceLoadResult> LoadAsync(string path, string? targetFramework, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(path);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var existing = Existing(full);

            if (existing is not null && SameFramework(existing.Load.TargetFramework, targetFramework))
            {
                existing.Touch();

                return existing.Load;
            }

            var loaded = await AddAsync(
                Key(existing, full),
                WorkspaceSeed.Fresh(watch, targetFramework),
                cancellationToken).ConfigureAwait(false);

            existing?.Dispose();

            return loaded.Load;
        }
        finally
        {
            gate.Release();
            ReclaimIfRequested();
        }
    }

    private static bool SameFramework(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<LoadedWorkspace> All() => Snapshot();

    public bool Unload(string path, bool reclaim = true)
    {
        var full = Path.GetFullPath(path);
        LoadedWorkspace? workspace;

        lock (map)
        {
            if (!workspaces.Remove(full, out workspace))
                return false;
        }

        FixerCatalog.Clear();
        workspace.Dispose();

        if (reclaim)
            Reclaim();

        return true;
    }

    public Result<WorkspaceLease> Resolve(string? workspaceHint, string? pathHint) =>
        Resolve(workspaceHint, pathHint, semantic: true);

    public Result<WorkspaceLease> Resolve(string? workspaceHint, string? pathHint, bool semantic)
    {
        lock (map)
        {
            var loaded = workspaces.Values.ToArray();

            if (loaded.Length is 0)
                return Result.Fail<WorkspaceLease>(Errors.NotLoaded());

            if (!string.IsNullOrWhiteSpace(workspaceHint))
                return ByHint(loaded, workspaceHint, semantic);

            return ByPath(loaded, pathHint, semantic) ?? Single(loaded, semantic);
        }
    }

    public void Dispose()
    {
        foreach (var workspace in Drain())
            workspace.Dispose();

        gate.Dispose();
    }

    private LoadedWorkspace[] Snapshot()
    {
        lock (map)
            return [.. workspaces.Values];
    }

    private LoadedWorkspace[] Drain()
    {
        lock (map)
        {
            var all = workspaces.Values.ToArray();

            workspaces.Clear();

            return all;
        }
    }

    private async Task<LoadedWorkspace> AddAsync(string full, WorkspaceSeed seed, CancellationToken cancellationToken)
    {
        var loaded = await WorkspaceLoader.LoadAsync(full, seed, cancellationToken).ConfigureAwait(false);
        var evicted = Store(full, loaded);

        if (evicted.Length > 0)
        {
            FixerCatalog.Clear();
            Interlocked.Exchange(ref reclaimRequested, 1);
        }

        foreach (var workspace in evicted)
            workspace.Dispose();

        return loaded;
    }

    private static void Reclaim()
    {
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    private LoadedWorkspace[] Store(string full, LoadedWorkspace loaded)
    {
        lock (map)
        {
            workspaces[full] = loaded;

            var evicted = new List<LoadedWorkspace>();

            while (workspaces.Count > maxWorkspaces)
            {
                var oldest = workspaces.MinBy(entry => entry.Value.LastUsedUtc);

                workspaces.Remove(oldest.Key);
                evicted.Add(oldest.Value);
            }

            return [.. evicted];
        }
    }

    private static Result<WorkspaceLease> ByHint(LoadedWorkspace[] loaded, string hint, bool semantic)
    {
        var best = Best(loaded, hint);

        return best switch
        {
            [var only] => Ok(only, semantic),
            [] => Result.Fail<WorkspaceLease>(Errors.WorkspaceNotFound(hint, Names(loaded))),
            _ => Result.Fail<WorkspaceLease>(Errors.AmbiguousWorkspace(Names(best))),
        };
    }

    private static Result<WorkspaceLease>? ByPath(LoadedWorkspace[] loaded, string? pathHint, bool semantic)
    {
        if (string.IsNullOrWhiteSpace(pathHint))
            return null;

        var matches = loaded.Where(workspace => workspace.Contains(pathHint)).ToArray();

        return matches.Length is 0 ? null : Ok(matches.MaxBy(workspace => workspace.Root.Length)!, semantic);
    }

    private static Result<WorkspaceLease> Single(LoadedWorkspace[] loaded, bool semantic) =>
        loaded.Length is 1
            ? Ok(loaded[0], semantic)
            : Result.Fail<WorkspaceLease>(Errors.AmbiguousWorkspace(Names(loaded)));

    private static int Tier(LoadedWorkspace workspace, string hint)
    {
        if (Path.IsPathRooted(hint) && PathBoundary.SameFile(workspace.SolutionPath, Path.GetFullPath(hint)))
            return 0;

        if (Same(Path.GetFileName(workspace.SolutionPath), hint))
            return 1;

        if (Same(Path.GetFileNameWithoutExtension(workspace.SolutionPath), hint))
            return 2;

        if (Same(workspace.Git.WorktreeName, hint))
            return 3;

        if (Same(Path.GetFileName(workspace.Root), hint))
            return 4;

        return workspace.SolutionPath.Contains(hint, StringComparison.OrdinalIgnoreCase) ? 5 : int.MaxValue;
    }

    private static Result<WorkspaceLease> Ok(LoadedWorkspace workspace, bool semantic)
    {
        workspace.Touch(semantic);

        return Result.Ok(workspace.Lease());
    }

    private static LoadedWorkspace[] Best(LoadedWorkspace[] loaded, string hint)
    {
        var ranked = loaded.Select(workspace => (Workspace: workspace, Tier: Tier(workspace, hint)))
            .Where(entry => entry.Tier is not int.MaxValue)
            .ToArray();

        return ranked.Length is 0
            ? []
            : [.. ranked.Where(entry => entry.Tier == ranked.Min(other => other.Tier)).Select(entry => entry.Workspace)];
    }

    private static bool Same(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static string[] Names(LoadedWorkspace[] loaded) =>
        [.. loaded.Select(workspace => Path.GetFileName(workspace.SolutionPath) + " (" + workspace.Git.WorktreeName + ") -> " + workspace.SolutionPath)];

    public async Task<WorkspaceLoadResult> ReloadAsync(string path, CancellationToken cancellationToken) =>
        (await SwapAsync(Path.GetFullPath(path), null, cancellationToken).ConfigureAwait(false)).Load;

    private WorkspaceSeed Seed(LoadedWorkspace? previous) => previous is null
        ? WorkspaceSeed.Fresh(watch)
        : new WorkspaceSeed(
            previous.Sync.Generations.Reloaded(),
            watch,
            previous.UndoNote(),
            previous.Load.TargetFramework);
    public async Task<WorkspaceLoadResult> RefreshAsync(LoadedWorkspace stale, CancellationToken cancellationToken) =>
        (await SwapAsync(Path.GetFullPath(stale.SolutionPath), stale, cancellationToken).ConfigureAwait(false)).Load; private async Task<LoadedWorkspace> SwapAsync(string full, LoadedWorkspace? stale, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var previous = Existing(full);

            if (stale is not null && !ReferenceEquals(previous, stale))
                return previous ?? stale;

            var loaded = await AddAsync(Key(previous, full), Seed(previous), cancellationToken).ConfigureAwait(false);

            previous?.Dispose();

            return loaded;
        }
        finally
        {
            gate.Release();
            ReclaimIfRequested();
        }
    }

    private LoadedWorkspace? Existing(string full)
    {
        lock (map)
        {
            if (workspaces.TryGetValue(full, out var direct))
                return direct;

            var real = PathBoundary.RealPath(full);

            return workspaces.Values.FirstOrDefault(workspace =>
                PathBoundary.SameFile(workspace.SolutionPath, full)
                || PathBoundary.RealPath(workspace.SolutionPath).Equals(real, PathBoundary.Comparison));
        }
    }

    private static string Key(LoadedWorkspace? previous, string full) =>
        previous is null ? full : Path.GetFullPath(previous.SolutionPath);

    private int reclaimRequested;

    private void ReclaimIfRequested()
    {
        if (Interlocked.Exchange(ref reclaimRequested, 0) is not 0)
            Reclaim();
    }

    public int DropIdleCompilations(TimeSpan idleFor) =>
        DropIdleCompilations(idleFor, GC.GetTotalMemory(forceFullCollection: false));

    internal int DropIdleCompilations(TimeSpan idleFor, long managedBytes)
    {
        if (idleFor <= TimeSpan.Zero)
            return 0;

        var pressured = managedBytes >= PressureBytes;
        var dropped = 0;

        foreach (var workspace in Snapshot())
        {
            if (Releasable(workspace, idleFor, pressured) && workspace.DropCompilations())
                dropped++;
        }

        if (dropped > 0)
            Reclaim();

        return dropped;
    }

    private static bool Releasable(LoadedWorkspace workspace, TimeSpan idleFor, bool pressured) =>
        !workspace.CompilationsDropped
        && workspace.Idle >= (pressured ? MinimumIdle : idleFor);

    private const long PressureBytes = 2L * 1024 * 1024 * 1024;
    private static readonly TimeSpan MinimumIdle = TimeSpan.FromMinutes(1);
}
