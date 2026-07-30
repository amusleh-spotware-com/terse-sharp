namespace TerseSharp.Core;

public sealed class WorkspaceRegistry(int maxWorkspaces = 4) : IDisposable
{
    private readonly Dictionary<string, LoadedWorkspace> workspaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly int maxWorkspaces = maxWorkspaces;

    public async Task<WorkspaceLoadResult> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(path);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (workspaces.TryGetValue(full, out var existing))
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

    public IReadOnlyList<LoadedWorkspace> All() => [.. workspaces.Values];

    public bool Unload(string path)
    {
        var full = Path.GetFullPath(path);

        if (!workspaces.Remove(full, out var workspace))
            return false;

        workspace.Dispose();

        return true;
    }

    public Result<LoadedWorkspace> Resolve(string? workspaceHint, string? pathHint)
    {
        if (workspaces.Count is 0)
            return Result.Fail<LoadedWorkspace>(Errors.NotLoaded());

        if (!string.IsNullOrWhiteSpace(workspaceHint))
            return ByHint(workspaceHint);

        var byPath = ByPath(pathHint);

        return byPath ?? Single();
    }

    public void Dispose()
    {
        foreach (var workspace in workspaces.Values)
            workspace.Dispose();

        workspaces.Clear();
        gate.Dispose();
    }

    private async Task<LoadedWorkspace> AddAsync(string full, CancellationToken cancellationToken)
    {
        var loaded = await WorkspaceLoader.LoadAsync(full, cancellationToken).ConfigureAwait(false);

        workspaces[full] = loaded;
        EvictOldest();

        return loaded;
    }

    private void EvictOldest()
    {
        while (workspaces.Count > maxWorkspaces)
        {
            var oldest = workspaces.MinBy(entry => entry.Value.LastUsedUtc);

            workspaces.Remove(oldest.Key);
            oldest.Value.Dispose();
        }
    }

    private Result<LoadedWorkspace> ByHint(string hint)
    {
        var matches = workspaces.Values
            .Where(workspace => Matches(workspace, hint))
            .ToArray();

        return matches.Length switch
        {
            1 => Ok(matches[0]),
            0 => Result.Fail<LoadedWorkspace>(Errors.WorkspaceNotFound(hint, Paths())),
            _ => Result.Fail<LoadedWorkspace>(Errors.AmbiguousWorkspace(Paths())),
        };
    }

    private Result<LoadedWorkspace>? ByPath(string? pathHint)
    {
        if (string.IsNullOrWhiteSpace(pathHint))
            return null;

        var matches = workspaces.Values.Where(workspace => workspace.Contains(pathHint)).ToArray();

        return matches.Length is 1 ? Ok(matches[0]) : null;
    }

    private Result<LoadedWorkspace> Single() =>
        workspaces.Count is 1
            ? Ok(workspaces.Values.First())
            : Result.Fail<LoadedWorkspace>(Errors.AmbiguousWorkspace(Paths()));

    private static bool Matches(LoadedWorkspace workspace, string hint) =>
        workspace.SolutionPath.Contains(hint, StringComparison.OrdinalIgnoreCase)
        || workspace.Git.WorktreeName.Equals(hint, StringComparison.OrdinalIgnoreCase);

    private static Result<LoadedWorkspace> Ok(LoadedWorkspace workspace)
    {
        workspace.Touch();

        return Result.Ok(workspace);
    }

    private string[] Paths() => [.. workspaces.Keys];
}
