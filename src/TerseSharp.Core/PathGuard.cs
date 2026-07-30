namespace TerseSharp.Core;

public static class PathGuard
{
    public static Result<string> Resolve(LoadedWorkspace workspace, string path)
    {
        var full = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(workspace.Root, path));

        return full.StartsWith(workspace.Root, StringComparison.OrdinalIgnoreCase)
            ? Result.Ok(full)
            : Result.Fail<string>(Errors.OutOfWorkspace(full));
    }
}
