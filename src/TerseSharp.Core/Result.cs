namespace TerseSharp.Core;

public readonly record struct Result<TValue>(TValue? Value, TerseError? Error)
{
    public bool IsOk => Error is null;
}

public static class Result
{
    public static Result<TValue> Ok<TValue>(TValue value) => new(value, null);

    public static Result<TValue> Fail<TValue>(TerseError error) => new(default, error);
}

public static class Errors
{
    public static TerseError NotLoaded() => new(
        TerseErrorCode.WorkspaceNotLoaded,
        "no workspace is loaded",
        "call load_workspace(path) or start the server inside a solution directory");

    public static TerseError WorkspaceNotFound(string hint, IReadOnlyList<string> loaded) => new(
        TerseErrorCode.WorkspaceNotFound,
        string.Create(CultureInfo.InvariantCulture, $"no loaded workspace matches '{hint}'"),
        "loaded: " + string.Join(", ", loaded));

    public static TerseError AmbiguousWorkspace(IReadOnlyList<string> loaded) => new(
        TerseErrorCode.AmbiguousWorkspace,
        "several workspaces are loaded and the request does not identify one",
        "pass workspace=<path or worktree name>; loaded: " + string.Join(", ", loaded));

    public static TerseError SymbolNotFound(string symbolId, IReadOnlyList<string> nearest) => new(
        TerseErrorCode.SymbolNotFound,
        string.Create(CultureInfo.InvariantCulture, $"symbol '{symbolId}' did not resolve"),
        nearest.Count is 0 ? "use search_symbols to find the id" : "nearest: " + string.Join(", ", nearest));

    public static TerseError AmbiguousSymbol(string symbolId, IReadOnlyList<string> candidates) => new(
        TerseErrorCode.AmbiguousSymbol,
        string.Create(CultureInfo.InvariantCulture, $"symbol '{symbolId}' resolves in {candidates.Count} places"),
        "pass workspace= to narrow it; candidates: " + string.Join(", ", candidates));

    public static TerseError DocumentNotFound(string path) => new(
        TerseErrorCode.DocumentNotFound,
        string.Create(CultureInfo.InvariantCulture, $"'{path}' is not a document in the loaded workspace"),
        "check the path, or use find_files to locate it");

    public static TerseError OutOfWorkspace(string path) => new(
        TerseErrorCode.OutOfWorkspace,
        string.Create(CultureInfo.InvariantCulture, $"'{path}' resolves outside the workspace root"),
        "pass a path inside the loaded workspace");

    public static TerseError CompileRegression(IReadOnlyList<string> diagnostics) => new(
        TerseErrorCode.CompileRegression,
        "the edit introduced compile errors and was rolled back:\n" + string.Join("\n", diagnostics),
        "fix the edit, or pass allowErrors=true to apply it anyway");

    public static TerseError EditConflict(string message) => new(
        TerseErrorCode.EditConflict,
        message,
        "re-read the target and retry");

    public static TerseError Invalid(string message, string remedy) =>
        new(TerseErrorCode.InvalidArgument, message, remedy);
}
