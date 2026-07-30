namespace TerseSharp.Core;

public static class SourceFile
{
    public static Result<string>? Reject(string path, bool force)
    {
        if (force || !IsCSharp(path))
            return null;

        return Result.Fail<string>(Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"'{path}' is a C# file"),
            "use replace_symbol_body, replace_symbol, add_member or rename_symbol so the edit is compile-gated, or pass force=true"));
    }

    private static bool IsCSharp(string path) =>
        Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase);
}
