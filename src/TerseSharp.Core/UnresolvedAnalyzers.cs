namespace TerseSharp.Core;

public sealed record UnresolvedAnalyzers(int ProjectCount, IReadOnlyList<string> Paths)
{
    public static readonly UnresolvedAnalyzers None = new(0, []);

    public bool Any => Paths.Count > 0;
}
