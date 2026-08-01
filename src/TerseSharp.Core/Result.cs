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
        "pass workspace=<solution file name, worktree name or full path>; loaded: " + string.Join(" | ", loaded));

    public static TerseError SymbolNotFound(string symbolId, IReadOnlyList<string> nearest) => new(
        TerseErrorCode.SymbolNotFound,
        string.Create(CultureInfo.InvariantCulture, $"symbol '{symbolId}' did not resolve"),
        nearest.Count is 0 ? "use search_symbols to find the id" : "nearest: " + string.Join(", ", nearest));

    public static TerseError AmbiguousSymbol(string symbolId, IReadOnlyList<string> candidates) => new(
        TerseErrorCode.AmbiguousSymbol,
        string.Create(CultureInfo.InvariantCulture, $"symbol '{symbolId}' resolves in {candidates.Count} places"),
        "pass workspace= to narrow it; candidates: " + string.Join(", ", candidates));

    public static TerseError AmbiguousName(string name, IReadOnlyList<string> candidates, int total) => new(
        TerseErrorCode.AmbiguousSymbol,
        string.Create(CultureInfo.InvariantCulture, $"name '{name}' resolves to {total} symbols"),
        string.Create(
            CultureInfo.InvariantCulture,
            $"pass one of these ids, or qualify the name with its containing type (showing {candidates.Count} of {total}): {string.Join(", ", candidates)}"));

    public static TerseError SaturatedName(string name, int cap) => new(
        TerseErrorCode.AmbiguousSymbol,
        string.Create(CultureInfo.InvariantCulture, $"name '{name}' matches more than {cap} symbols, so it cannot be resolved safely"),
        "qualify the name with its containing type, or pass the documentation id from search_symbols");

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

    public static TerseError Cancelled() => new(
        TerseErrorCode.Cancelled,
        "the call was cancelled before it produced a result",
        "retry, or narrow the request with a path or maxResults");

    public static TerseError Blank(string name) => new(
        TerseErrorCode.InvalidArgument,
        string.Create(CultureInfo.InvariantCulture, $"'{name}' is required and cannot be empty"),
        string.Create(CultureInfo.InvariantCulture, $"pass a non-empty {name}"));
}
