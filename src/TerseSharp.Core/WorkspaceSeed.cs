namespace TerseSharp.Core;

internal readonly record struct WorkspaceSeed(
    WorkspaceGenerations Generations,
    bool Watch,
    string? UndoNote,
    string? TargetFramework = null)
{
    public static WorkspaceSeed Fresh(bool watch) => new(default, watch, null);

    public static WorkspaceSeed Fresh(bool watch, string? targetFramework) => new(default, watch, null, targetFramework);
}
