namespace TerseSharp.Core;

public static class PathGuard
{
    public static Result<string> Resolve(LoadedWorkspace workspace, string path)
    {
        var full = Full(workspace.Root, path);

        return workspace.Contains(full)
            ? Result.Ok(full)
            : Result.Fail<string>(Errors.OutOfWorkspace(full));
    }

    public static Result<string> Resolve(string root, string path)
    {
        var full = Full(root, path);

        return PathBoundary.Contains(root, full)
            ? Result.Ok(full)
            : Result.Fail<string>(Errors.OutOfWorkspace(full));
    }

    public static string Full(string root, string path) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(root, path));
}
