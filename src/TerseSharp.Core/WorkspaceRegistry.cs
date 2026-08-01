namespace TerseSharp.Core;

public sealed class WorkspaceRegistry(int maxWorkspaces = 4, bool watch = true) : IDisposable
{
    private readonly Dictionary<string, LoadedWorkspace> workspaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock map = new();
    private readonly int maxWorkspaces = maxWorkspaces;
    private readonly bool watch = watch;

    public async Task<WorkspaceLoadResult> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(path);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (TryGet(full) is { } existing)
            {
                existing.Touch();

                return existing.Load;
            }

            return (await AddAsync(full, WorkspaceSeed.Fresh(watch), cancellationToken).ConfigureAwait(false)).Load;
        }
        finally
        {
            gate.Release();
        }
    }

    public IReadOnlyList<LoadedWorkspace> All() => Snapshot();

    public bool Unload(string path)
    {
        var full = Path.GetFullPath(path);
        LoadedWorkspace? workspace;

        lock (map)
        {
            if (!workspaces.Remove(full, out workspace))
                return false;
        }

        workspace.Dispose();

        return true;
    }

    public Result<WorkspaceLease> Resolve(string? workspaceHint, string? pathHint)
    {
        lock (map)
        {
            var loaded = workspaces.Values.ToArray();

            if (loaded.Length is 0)
                return Result.Fail<WorkspaceLease>(Errors.NotLoaded());

            if (!string.IsNullOrWhiteSpace(workspaceHint))
                return ByHint(loaded, workspaceHint);

            return ByPath(loaded, pathHint) ?? Single(loaded);
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

    private LoadedWorkspace? TryGet(string full)
    {
        lock (map)
            return workspaces.TryGetValue(full, out var existing) ? existing : null;
    }

    private async Task<LoadedWorkspace> AddAsync(string full, WorkspaceSeed seed, CancellationToken cancellationToken)
    {
        var loaded = await WorkspaceLoader.LoadAsync(full, seed, cancellationToken).ConfigureAwait(false);

        foreach (var evicted in Store(full, loaded))
            evicted.Dispose();

        return loaded;
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

    private static Result<WorkspaceLease> ByHint(LoadedWorkspace[] loaded, string hint)
    {
        var best = Best(loaded, hint);

        return best switch
        {
            [var only] => Ok(only),
            [] => Result.Fail<WorkspaceLease>(Errors.WorkspaceNotFound(hint, Names(loaded))),
            _ => Result.Fail<WorkspaceLease>(Errors.AmbiguousWorkspace(Names(best))),
        };
    }

    private static Result<WorkspaceLease>? ByPath(LoadedWorkspace[] loaded, string? pathHint)
    {
        if (string.IsNullOrWhiteSpace(pathHint))
            return null;

        var matches = loaded.Where(workspace => workspace.Contains(pathHint)).ToArray();

        return matches.Length is 0 ? null : Ok(matches.MaxBy(workspace => workspace.Root.Length)!);
    }

    private static Result<WorkspaceLease> Single(LoadedWorkspace[] loaded) =>
        loaded.Length is 1
            ? Ok(loaded[0])
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

    private static Result<WorkspaceLease> Ok(LoadedWorkspace workspace)
    {
        workspace.Touch();

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
        : new WorkspaceSeed(previous.Sync.Generations.Reloaded(), watch, previous.UndoNote());
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
        }
    }

    private LoadedWorkspace? Existing(string full)
    {
        lock (map)
        {
            return workspaces.TryGetValue(full, out var direct)
                ? direct
                : workspaces.Values.FirstOrDefault(workspace => PathBoundary.SameFile(workspace.SolutionPath, full));
        }
    }

    private static string Key(LoadedWorkspace? previous, string full) =>
        previous is null ? full : Path.GetFullPath(previous.SolutionPath);
}
