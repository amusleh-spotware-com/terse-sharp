namespace TerseSharp.Core;

public static class SourceFile
{
    public static Result<string>? Reject(string path, string full, bool force) =>
        force || !IsCSharp(path)
            ? null
            : Refused(string.Create(CultureInfo.InvariantCulture, $"'{path}' is a C# file"), Remedy(full));

    public static Result<string>? Reject(LoadedWorkspace workspace, string path, string full, bool force) =>
        force || !IsCSharp(path) || DocumentLookup.Find(workspace, path) is not null
            ? null
            : File.Exists(full)
                ? Refused(string.Create(CultureInfo.InvariantCulture, $"'{path}' is a C# file no loaded project compiles"), UncompiledRemedy)
                : Reject(path, full, force: false);

    private static string Remedy(string full) => File.Exists(full)
        ? "use replace_symbol_body, replace_symbol, add_member or rename_symbol so the edit is compile-gated, or pass force=true"
        : "the file does not exist yet, and no symbol tool can create one: create it with write_text force=true, then add_member or replace_symbol - the new file is part of the workspace immediately";

    public static bool IsCSharp(ReadOnlySpan<char> path) =>
        Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase);

    private const string UncompiledRemedy = "no symbol tool can address a file no loaded project compiles: pass force=true to edit it as plain text - nothing compiles it, so nothing gates it - or load_workspace the solution whose project compiles it and edit it there with replace_symbol_body, replace_symbol or add_member";

    private static Result<string> Refused(string message, string remedy) => Result.Fail<string>(Errors.Invalid(message, remedy));
}
