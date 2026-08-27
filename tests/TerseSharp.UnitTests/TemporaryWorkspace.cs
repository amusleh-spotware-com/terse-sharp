using Microsoft.CodeAnalysis;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class TemporaryWorkspace : IDisposable
{
    private readonly TemporarySolution files;
    private readonly WorkspaceRegistry registry;

    private WorkspaceLease lease;

    private TemporaryWorkspace(TemporarySolution files, WorkspaceRegistry registry, WorkspaceLease lease)
    {
        this.files = files;
        this.registry = registry;
        this.lease = lease;
    }

    public TemporarySolution Files => files;

    public LoadedWorkspace Workspace => lease.Workspace;

    public WorkspaceSync Sync => Workspace.Sync;

    public static async Task<TemporaryWorkspace> OpenAsync(CancellationToken cancellationToken)
    {
        var files = TemporarySolution.Create();
        var registry = new WorkspaceRegistry(watch: false);

        await registry.LoadAsync(files.SolutionPath, cancellationToken);

        return new TemporaryWorkspace(files, registry, registry.Resolve(null, null).Value!);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        lease.Dispose();

        await registry.ReloadAsync(files.SolutionPath, cancellationToken);

        lease = registry.Resolve(null, null).Value!;
    }

    public Task<bool> SyncAsync(string? pathHint, CancellationToken cancellationToken) =>
        Sync.SyncAsync(Workspace, pathHint, cancellationToken);

    public Document Document(string name) => Workspace.Solution.Projects
        .Single()
        .Documents
        .Single(document => string.Equals(document.Name, name, StringComparison.Ordinal));

    public async Task MaterialiseAsync(CancellationToken cancellationToken)
    {
        materialised.Clear();

        foreach (var document in Workspace.Solution.Projects.SelectMany(project => project.Documents))
            materialised.Add(await document.GetTextAsync(cancellationToken));
    }

    public void Dispose()
    {
        lease.Dispose();
        registry.Dispose();
        files.Dispose();
    }

    private readonly List<Microsoft.CodeAnalysis.Text.SourceText> materialised = [];
}
