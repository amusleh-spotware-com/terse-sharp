using System.Diagnostics;

namespace TerseSharp.Core;

internal readonly record struct GateScope(bool Narrowed, int Projects)
{
    public string Name => (Projects, Narrowed) switch
    {
        (0, _) => "none",
        (_, true) => "project",
        _ => "dependents",
    };
}

internal readonly record struct AnalysisTiming(TimeSpan Shape, TimeSpan Realize, TimeSpan Compile, TimeSpan Hints, GateScope Scope);

internal readonly record struct GateTiming(TimeSpan Total, TimeSpan Diff, AnalysisTiming Analysis, TimeSpan Policy, TimeSpan Apply)
{
    public const string Variable = "TERSE_GATE_TIMING_MS";

    public static TimeSpan SlowAfter { get; } = Configured(Environment.GetEnvironmentVariable(Variable));

    public static GateTiming Since(long started, long diffed, long analysed, long policed, AnalysisTiming analysis) =>
        new(Stopwatch.GetElapsedTime(started), Stopwatch.GetElapsedTime(started, diffed), analysis, Stopwatch.GetElapsedTime(analysed, policed), Stopwatch.GetElapsedTime(policed));

    public string Appended(string response, TimeSpan slowAfter) => Total > slowAfter
        ? string.Create(CultureInfo.InvariantCulture, $"{response}\ntiming totalMs={WholeMilliseconds(Total)} diffMs={WholeMilliseconds(Diff)} shapeMs={WholeMilliseconds(Analysis.Shape)} realizeMs={WholeMilliseconds(Analysis.Realize)} compileMs={WholeMilliseconds(Analysis.Compile)} hintsMs={WholeMilliseconds(Analysis.Hints)} policyMs={WholeMilliseconds(Policy)} applyMs={WholeMilliseconds(Apply)} gate={Analysis.Scope.Name} projects={Analysis.Scope.Projects}")
        : response;

    internal static TimeSpan Configured(string? milliseconds) => long.TryParse(milliseconds, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed <= int.MaxValue
        ? TimeSpan.FromMilliseconds(parsed)
        : TimeSpan.FromSeconds(10);

    private static long WholeMilliseconds(TimeSpan span) => (long)span.TotalMilliseconds;
}
