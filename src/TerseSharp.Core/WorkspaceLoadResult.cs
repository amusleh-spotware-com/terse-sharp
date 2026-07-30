namespace TerseSharp.Core;

public sealed record WorkspaceLoadResult(
    string SolutionPath,
    int ProjectCount,
    int DocumentCount,
    long ElapsedMilliseconds,
    IReadOnlyList<string> Failures);
