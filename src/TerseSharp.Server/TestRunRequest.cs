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
    ImmutableArray<string> Targets = default,
    int Parallel = 0,
    ImmutableArray<string> RunSettings = default,
    TestReporter Reporter = TestReporter.VsTestLogger)
{
    public bool WantsDetail => Verbose || IncludePassed || Slowest > 0;

    public ImmutableArray<string> Invocations => Targets.IsDefaultOrEmpty ? [Target] : Targets;

    public bool IsSerial => Invocations.Length is 1 || Parallel is 1;

    public int Degree => IsSerial
        ? 1
        : Math.Clamp(Parallel is 0 ? Environment.ProcessorCount : Parallel, 1, Invocations.Length);
}
