namespace TerseSharp.Server;

internal readonly record struct TestRunRequest(
    string Target,
    string? Filter,
    bool NoBuild,
    bool IncludePassed,
    int Slowest,
    TimeSpan Timeout,
    bool Verbose = false)
{
    public bool WantsDetail => Verbose || IncludePassed || Slowest > 0;
}
