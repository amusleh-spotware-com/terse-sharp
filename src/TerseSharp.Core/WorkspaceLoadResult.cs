namespace TerseSharp.Core;

public sealed record WorkspaceLoadResult(
    string SolutionPath,
    int ProjectCount,
    int DocumentCount,
    long ElapsedMilliseconds,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> Warnings,
    string? TargetFramework = null,
    UnresolvedAnalyzers? Analyzers = null)
{
    public UnresolvedAnalyzers Unresolved => Analyzers ?? UnresolvedAnalyzers.None;
}
