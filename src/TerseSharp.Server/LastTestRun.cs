using System.Collections.Immutable;

namespace TerseSharp.Server;

public sealed record TestRunMemory(
    string WorkspaceRoot,
    string Target,
    ImmutableArray<string> FailedTests,
    BuildScope Scope = default,
    bool Unfiltered = false)
{
    public bool Covers(string workspaceRoot) =>
        WorkspaceRoot.Equals(workspaceRoot, PathBoundary.Comparison) && !FailedTests.IsDefaultOrEmpty;

    public TestRunMemory After(TestRunMemory run, IEnumerable<string> passedTests, int limit) =>
        Outstanding(run.WorkspaceRoot, passedTests) switch
        {
            [] => run with { FailedTests = Capped(run.FailedTests, limit) },
            _ when run.Unfiltered && SameInvocation(run) => run with { FailedTests = Capped(run.FailedTests, limit) },
            var outstanding when run.FailedTests.IsDefaultOrEmpty => this with { FailedTests = outstanding },
            var outstanding when SameInvocation(run) => run with { FailedTests = Capped([.. outstanding.Union(run.FailedTests, StringComparer.Ordinal)], limit) },
            _ => run with { FailedTests = Capped(run.FailedTests, limit) },
        };

    private ImmutableArray<string> Outstanding(string workspaceRoot, IEnumerable<string> passedTests) =>
        Covers(workspaceRoot)
            ? FailedTests.RemoveAll(new HashSet<string>(passedTests, StringComparer.Ordinal).Contains)
            : [];

    private bool SameInvocation(TestRunMemory run) =>
        Target.Equals(run.Target, StringComparison.Ordinal) && SameScope(Scope, run.Scope);

    private static bool SameScope(BuildScope left, BuildScope right) =>
        string.Equals(left.Configuration, right.Configuration, StringComparison.Ordinal)
        && string.Equals(left.TargetFramework, right.TargetFramework, StringComparison.Ordinal)
        && (left.Properties ?? []).SequenceEqual(right.Properties ?? [], StringComparer.Ordinal);

    private static ImmutableArray<string> Capped(ImmutableArray<string> tests, int limit) => tests switch
    {
        { IsDefault: true } => [],
        { Length: var length } when length > limit => ImmutableArray.Create(tests, 0, limit),
        _ => tests,
    };

    public static bool Whole(string? filter, ImmutableArray<string> targets, int total) =>
        filter is not { Length: > 0 } && targets.IsDefaultOrEmpty && total > 0;
}

public sealed class LastTestRun
{
    private const int MaxRememberedTests = 200;

    private const int MaxRememberedSolutions = 16;

    private static readonly TestRunMemory Nothing = new(string.Empty, string.Empty, []);

    private readonly Dictionary<string, (TestRunMemory Memory, long Order)> runs = new(StringComparer.FromComparison(PathBoundary.Comparison));
    private long order;
    private readonly Lock gate = new();

    public TestRunMemory Recall(string solutionPath)
    {
        lock (gate)
            return runs.TryGetValue(solutionPath, out var remembered) ? remembered.Memory : Nothing;
    }

    public ImmutableArray<string> Elsewhere(string solutionPath)
    {
        lock (gate)
        {
            var others = ImmutableArray.CreateBuilder<string>(runs.Count);

            foreach (var (path, remembered) in runs)
            {
                if (!path.Equals(solutionPath, PathBoundary.Comparison) && !remembered.Memory.FailedTests.IsDefaultOrEmpty)
                    others.Add(string.Create(CultureInfo.InvariantCulture, $"{path} ({remembered.Memory.FailedTests.Length})"));
            }

            return others.ToImmutable();
        }
    }

    public void Remember(string workspaceRoot, string target, IEnumerable<string> failedTests, BuildScope scope = default, IEnumerable<string>? passedTests = null, bool unfiltered = false, string? solutionPath = null)
    {
        var run = new TestRunMemory(workspaceRoot, target, [.. failedTests.Take(MaxRememberedTests)], scope, unfiltered);

        var key = solutionPath ?? workspaceRoot;

        lock (gate)
        {
            var previous = runs.TryGetValue(key, out var remembered) ? remembered.Memory : Nothing;

            runs[key] = (previous.After(run, passedTests ?? [], MaxRememberedTests), ++order);
            Evict();
        }
    }

    private void Evict()
    {
        if (runs.Count <= MaxRememberedSolutions)
            return;

        var oldest = string.Empty;
        var first = long.MaxValue;

        foreach (var (path, remembered) in runs)
        {
            if (remembered.Order < first)
                (oldest, first) = (path, remembered.Order);
        }

        runs.Remove(oldest);
    }
}
