namespace TerseSharp.Core;

public sealed class RazorRegistrationCache(LoadedWorkspace workspace)
{
    private RazorRegistrationIndex? index;

    public async ValueTask<RazorRegistrationIndex> GetAsync(CancellationToken cancellationToken) =>
        index ??= await RazorRegistrations.IndexAsync(workspace, cancellationToken).ConfigureAwait(false);
}
