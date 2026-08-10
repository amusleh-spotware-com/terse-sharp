using System.Collections.Immutable;

namespace TerseSharp.Server;

internal readonly record struct TestRunRequest(
    string Target,
    string? Filter,
    bool NoBuild,
    bool IncludePassed,
    int Slowest,
    TimeSpan Timeout,
    bool Verbose = false,
    BuildScope Scope = default,
    ImmutableArray<string> Targets = default)
{
    public bool WantsDetail => Verbose || IncludePassed || Slowest > 0;

    public ImmutableArray<string> Invocations => Targets.IsDefaultOrEmpty ? [Target] : Targets;
}
