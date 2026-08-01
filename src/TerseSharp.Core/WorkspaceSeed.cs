namespace TerseSharp.Core;

internal readonly record struct WorkspaceSeed(WorkspaceGenerations Generations, bool Watch, string? UndoNote)
{
    public static WorkspaceSeed Fresh(bool watch) => new(default, watch, null);
}
