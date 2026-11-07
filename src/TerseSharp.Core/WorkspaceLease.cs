namespace TerseSharp.Core;

public readonly record struct WorkspaceLease(LoadedWorkspace Workspace) : IDisposable
{
    public void Dispose() => Workspace.Release();

    public string? Note { get; init; }
}
