using Microsoft.CodeAnalysis;

namespace TerseSharp.Server;

public sealed class ToolContext(WorkspaceRegistry registry, bool readOnly) : IDisposable
{
    private Task ready = Task.CompletedTask;

    public WorkspaceRegistry Registry { get; } = registry;

    public bool ReadOnly { get; } = readOnly;

    public string? PreloadFailure { get; private set; }

    public void BeginPreload(string target, CancellationToken cancellationToken) =>
        Preload(Registry.LoadAsync(target, cancellationToken));

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
            var resolved = Registry.Resolve(workspace, null);

            if (!resolved.IsOk)
                return resolved.Error!.Render();

            using var lease = resolved.Value!;

            var symbol = await SymbolLookup.ResolveAsync(lease.Workspace, symbolId, cancellationToken).ConfigureAwait(false);

            return symbol.IsOk
                ? await action(lease.Workspace, symbol.Value!).ConfigureAwait(false)
                : symbol.Error!.Render();
        }).ConfigureAwait(false);
    }

    public async Task<string> WithWorkspace(string? workspace, string? pathHint, Func<LoadedWorkspace, string> action)
    {
        await ready.ConfigureAwait(false);

        return ToolBoundary.Run(() =>
        {
            var resolved = Registry.Resolve(workspace, pathHint);

            if (!resolved.IsOk)
                return resolved.Error!.Render();

            using var lease = resolved.Value!;

            return action(lease.Workspace);
        });
    }

    public async Task<string> WithWorkspaceAsync(
        string? workspace,
        string? pathHint,
        Func<LoadedWorkspace, Task<string>> action)
    {
        await ready.ConfigureAwait(false);

        return await ToolBoundary.RunAsync(async () =>
        {
            var resolved = Registry.Resolve(workspace, pathHint);

            if (!resolved.IsOk)
                return resolved.Error!.Render();

            using var lease = resolved.Value!;

            return await action(lease.Workspace).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public string? RejectWrite() => ReadOnly
        ? new TerseError(TerseErrorCode.ReadOnly, "the server is running with --read-only", "restart without --read-only").Render()
        : null;

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
}
