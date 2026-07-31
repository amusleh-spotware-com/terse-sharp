namespace TerseSharp.Server;

internal readonly record struct TestRunRequest(
    string Target,
    string? Filter,
    bool NoBuild,
    bool IncludePassed,
    int Slowest,
    TimeSpan Timeout);
