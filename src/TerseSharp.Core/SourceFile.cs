namespace TerseSharp.Core;

public static class SourceFile
{
    public static Result<string>? Reject(string path, string full, bool force)
    {
        if (force || !IsCSharp(path))
            return null;

        return Result.Fail<string>(Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"'{path}' is a C# file"),
            Remedy(full)));
    }

    public static Result<string>? Reject(LoadedWorkspace workspace, string path, string full, bool force) =>
        force || !IsCSharp(path) || DocumentLookup.Find(workspace, path) is not null
            ? null
            : Reject(path, full, force: false);

    private static string Remedy(string full) => File.Exists(full)
        ? "use replace_symbol_body, replace_symbol, add_member or rename_symbol so the edit is compile-gated, or pass force=true"
        : "the file does not exist yet, and no symbol tool can create one: call write_text(path, content, force=true), then add_member or replace_symbol - the new file is part of the workspace immediately";

    public static bool IsCSharp(ReadOnlySpan<char> path) =>
        Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase);
}
