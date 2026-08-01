namespace TerseSharp.Core;

public sealed record ResxTarget(ResxIndex Index, ResxFamily Family, string Path)
{
    public static Result<ResxTarget> Locate(LoadedWorkspace workspace, string path)
    {
        if (path is not { Length: > 0 })
            return Result.Fail<ResxTarget>(Errors.Blank("path"));

        var resolved = PathGuard.Resolve(workspace, path);

        if (!resolved.IsOk)
            return Result.Fail<ResxTarget>(resolved.Error!);

        var index = ResxIndex.Build(workspace.Root);
        var family = index.FamilyOf(resolved.Value!);

        return family is null
            ? Result.Fail<ResxTarget>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{path}' is not a .resx or .resw file in this workspace"),
                "call resx_files to list the resource families"))
            : Result.Ok(new ResxTarget(index, family, resolved.Value!));
    }
}
