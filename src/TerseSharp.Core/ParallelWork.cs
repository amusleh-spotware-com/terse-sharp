namespace TerseSharp.Core;

internal static class ParallelWork
{
    public static ParallelOptions Options(CancellationToken cancellationToken) => new()
    {
        MaxDegreeOfParallelism = Environment.ProcessorCount,
        CancellationToken = cancellationToken,
    };

    public static ParallelOptions Sequential(CancellationToken cancellationToken) => new()
    {
        MaxDegreeOfParallelism = 1,
        CancellationToken = cancellationToken,
    };
}
