namespace TerseSharp.Core;

public static class PathGuard
{
    public static Result<string> Resolve(LoadedWorkspace workspace, string path) => Resolve(workspace.Root, path);

    public static Result<string> Resolve(string root, string path)
    {
        var full = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(root, path));

        return PathBoundary.Contains(root, full)
            ? Result.Ok(full)
            : Result.Fail<string>(Errors.OutOfWorkspace(full));
    }
}
