using Microsoft.CodeAnalysis;

namespace TerseSharp.Server;

public sealed class ToolContext(WorkspaceRegistry registry, bool readOnly) : IDisposable
{
    public WorkspaceRegistry Registry { get; } = registry;

    public bool ReadOnly { get; } = readOnly;

    public async Task<string> WithSymbolAsync(
        string? workspace,
        string symbolId,
        Func<LoadedWorkspace, ISymbol, Task<string>> action,
        CancellationToken cancellationToken)
    {
        return await ToolBoundary.RunAsync(async () =>
        {
            var resolved = Registry.Resolve(workspace, null);

            if (!resolved.IsOk)
                return resolved.Error!.Render();

            var symbol = await SymbolLookup.ResolveAsync(resolved.Value!, symbolId, cancellationToken).ConfigureAwait(false);

            return symbol.IsOk
                ? await action(resolved.Value!, symbol.Value!).ConfigureAwait(false)
                : symbol.Error!.Render();
        }).ConfigureAwait(false);
    }

    public string WithWorkspace(string? workspace, string? pathHint, Func<LoadedWorkspace, string> action)
    {
        return ToolBoundary.Run(() =>
        {
            var resolved = Registry.Resolve(workspace, pathHint);

            return resolved.IsOk ? action(resolved.Value!) : resolved.Error!.Render();
        });
    }

    public async Task<string> WithWorkspaceAsync(
        string? workspace,
        string? pathHint,
        Func<LoadedWorkspace, Task<string>> action)
    {
        return await ToolBoundary.RunAsync(async () =>
        {
            var resolved = Registry.Resolve(workspace, pathHint);

            return resolved.IsOk ? await action(resolved.Value!).ConfigureAwait(false) : resolved.Error!.Render();
        }).ConfigureAwait(false);
    }

    public string? RejectWrite() => ReadOnly
        ? new TerseError(TerseErrorCode.ReadOnly, "the server is running with --read-only", "restart without --read-only").Render()
        : null;

    public void Dispose() => Registry.Dispose();
}
