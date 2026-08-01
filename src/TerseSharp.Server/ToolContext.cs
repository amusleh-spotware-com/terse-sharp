using Microsoft.CodeAnalysis;

namespace TerseSharp.Server;

public sealed class ToolContext(WorkspaceRegistry registry, bool readOnly) : IDisposable
{
    private Task ready = Task.CompletedTask;

    public WorkspaceRegistry Registry { get; } = registry;

    public bool ReadOnly { get; } = readOnly;

    public string? PreloadFailure { get; private set; }

    public void BeginPreload(string target, CancellationToken cancellationToken) =>
        Preload(Task.Run(() => Registry.LoadAsync(target, cancellationToken), cancellationToken));

    public Task ReadyAsync() => ready;

    internal void Preload(Task load) => ready = ObserveAsync(load);

    public async Task<string> WithSymbolAsync(
        string? workspace,
        string symbolId,
        Func<LoadedWorkspace, ISymbol, Task<string>> action,
        CancellationToken cancellationToken)
    {
        await ready.ConfigureAwait(false);

        return await ToolBoundary.RunAsync(async () =>
        {
            var resolved = await ResolveAsync(workspace, null, semantic: true, cancellationToken).ConfigureAwait(false);

            if (!resolved.IsOk)
                return resolved.Error!.Render();

            using var lease = resolved.Value!;

            var symbol = await SymbolLookup.ResolveAsync(lease.Workspace, symbolId, cancellationToken).ConfigureAwait(false);

            return symbol.IsOk
                ? await action(lease.Workspace, symbol.Value!).ConfigureAwait(false)
                : symbol.Error!.Render();
        }).ConfigureAwait(false);
    }

    public Task<string> WithWorkspace(
        string? workspace,
        string? pathHint,
        Func<LoadedWorkspace, string> action,
        bool semantic = true,
        CancellationToken cancellationToken = default) =>
        WithWorkspaceAsync(workspace, pathHint, loaded => Task.FromResult(action(loaded)), semantic, cancellationToken);

    public async Task<string> WithWorkspaceAsync(
        string? workspace,
        string? pathHint,
        Func<LoadedWorkspace, Task<string>> action,
        bool semantic = true,
        CancellationToken cancellationToken = default)
    {
        await ready.ConfigureAwait(false);

        return await ToolBoundary.RunAsync(async () =>
        {
            var resolved = await ResolveAsync(workspace, pathHint, semantic, cancellationToken).ConfigureAwait(false);

            if (!resolved.IsOk)
                return resolved.Error!.Render();

            using var lease = resolved.Value!;

            return await action(lease.Workspace).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public string? RejectWrite() => ReadOnly
        ? new TerseError(TerseErrorCode.ReadOnly, "the server is running with --read-only", "restart without --read-only").Render()
        : null;

    public async Task<string> WithTargetAsync(
        string? workspace,
        string? pathHint,
        Func<WorkspaceTarget, Task<string>> action)
    {
        await ready.ConfigureAwait(false);

        return await ToolBoundary.RunAsync(async () =>
        {
            var target = Target(workspace, pathHint);

            return target.IsOk ? await action(target.Value!).ConfigureAwait(false) : target.Error!.Render();
        }).ConfigureAwait(false);
    }

    private Result<WorkspaceTarget> Target(string? workspace, string? pathHint)
    {
        var resolved = Registry.Resolve(workspace, pathHint);

        if (!resolved.IsOk)
            return Result.Fail<WorkspaceTarget>(resolved.Error!);

        using var lease = resolved.Value!;

        return Result.Ok(new WorkspaceTarget(lease.Workspace.SolutionPath, lease.Workspace.Root));
    }

    public void Dispose() => Registry.Dispose();

    private async Task ObserveAsync(Task load)
    {
        try
        {
            await load.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            PreloadFailure = "the workspace preload was cancelled";
        }
        catch (Exception exception)
        {
            PreloadFailure = string.Create(CultureInfo.InvariantCulture, $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private async Task<Result<WorkspaceLease>> ResolveAsync(
        string? workspace,
        string? pathHint,
        bool semantic,
        CancellationToken cancellationToken)
    {
        var resolved = Registry.Resolve(workspace, pathHint);

        if (!semantic || !resolved.IsOk)
            return resolved;

        return await SyncedAsync(resolved.Value!, workspace, pathHint, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<WorkspaceLease>> SyncedAsync(
        WorkspaceLease lease,
        string? workspace,
        string? pathHint,
        CancellationToken cancellationToken)
    {
        bool reload;

        try
        {
            reload = await lease.Workspace.Sync.SyncAsync(lease.Workspace, pathHint, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();

            throw;
        }

        if (!reload)
            return Result.Ok(lease);

        var stale = lease.Workspace;

        lease.Dispose();

        await Registry.RefreshAsync(stale, cancellationToken).ConfigureAwait(false);

        return Registry.Resolve(workspace, pathHint);
    }
}
