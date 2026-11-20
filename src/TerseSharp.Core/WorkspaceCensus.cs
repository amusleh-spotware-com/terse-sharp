namespace TerseSharp.Core;

public static class WorkspaceCensus
{
    public const string Remedy = "an unloaded or evicted workspace is still referenced after a compacting collection - a leak; a gcdump of this server names what holds it";

    private static readonly RetiredSet Workspaces = new();

    public static int Retained() => Workspaces.Retained(WorkspaceRegistry.Reclaim);

    public static string Detail(int retained) => retained is 0
        ? "retired=0 alive"
        : string.Create(CultureInfo.InvariantCulture, $"retired={retained} alive after a compacting collection");

    internal static void Retired(LoadedWorkspace workspace) => Workspaces.Add(workspace);
}
