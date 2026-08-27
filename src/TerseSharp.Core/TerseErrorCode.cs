namespace TerseSharp.Core;

public enum TerseErrorCode
{
    WorkspaceNotLoaded,
    WorkspaceNotFound,
    AmbiguousWorkspace,
    SymbolNotFound,
    AmbiguousSymbol,
    DocumentNotFound,
    ProjectNotFound,
    AmbiguousProject,
    EditConflict,
    CompileRegression,
    PolicyViolation,
    OutOfWorkspace,
    ReadOnly,
    InvalidArgument,
    Cancelled,
    Internal,
    Transient,
    UnsupportedRunner,
    NameTaken
}

public sealed record TerseError(TerseErrorCode Code, string Message, string Remedy)
{
    public string Render() =>
        string.Create(CultureInfo.InvariantCulture, $"ERROR {Code}: {Message}\nremedy: {Remedy}");
}
