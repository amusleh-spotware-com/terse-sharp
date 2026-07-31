namespace TerseSharp.Core;

public sealed class WorkspaceRegistry(int maxWorkspaces = 4) : IDisposable
{
    private readonly Dictionary<string, LoadedWorkspace> workspaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock map = new();
    private readonly int maxWorkspaces = maxWorkspaces;

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

            return (await AddAsync(full, cancellationToken).ConfigureAwait(false)).Load;
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

    private async Task<LoadedWorkspace> AddAsync(string full, CancellationToken cancellationToken)
    {
        var loaded = await WorkspaceLoader.LoadAsync(full, cancellationToken).ConfigureAwait(false);

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
        var matches = loaded.Where(workspace => Matches(workspace, hint)).ToArray();

        return matches.Length switch
        {
            1 => Ok(matches[0]),
            0 => Result.Fail<WorkspaceLease>(Errors.WorkspaceNotFound(hint, Paths(loaded))),
            _ => Result.Fail<WorkspaceLease>(Errors.AmbiguousWorkspace(Paths(loaded))),
        };
    }

    private static Result<WorkspaceLease>? ByPath(LoadedWorkspace[] loaded, string? pathHint)
    {
        if (string.IsNullOrWhiteSpace(pathHint))
            return null;

        var matches = loaded.Where(workspace => workspace.Contains(pathHint)).ToArray();

        return matches.Length is 1 ? Ok(matches[0]) : null;
    }

    private static Result<WorkspaceLease> Single(LoadedWorkspace[] loaded) =>
        loaded.Length is 1
            ? Ok(loaded[0])
            : Result.Fail<WorkspaceLease>(Errors.AmbiguousWorkspace(Paths(loaded)));

    private static bool Matches(LoadedWorkspace workspace, string hint) =>
        workspace.SolutionPath.Contains(hint, StringComparison.OrdinalIgnoreCase)
        || workspace.Git.WorktreeName.Equals(hint, StringComparison.OrdinalIgnoreCase);

    private static Result<WorkspaceLease> Ok(LoadedWorkspace workspace)
    {
        workspace.Touch();

        return Result.Ok(workspace.Lease());
    }

    private static string[] Paths(LoadedWorkspace[] loaded) => [.. loaded.Select(workspace => workspace.SolutionPath)];
}
