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

    private static string Remedy(string full) => File.Exists(full)
        ? "use replace_symbol_body, replace_symbol, add_member or rename_symbol so the edit is compile-gated, or pass force=true"
        : "the file does not exist yet, and no symbol tool can create one: call write_text(path, content, force=true), then add_member or replace_symbol - the new file is part of the workspace immediately";

    public static bool IsCSharp(string path) =>
        Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase);
}
