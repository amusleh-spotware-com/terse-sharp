using System.Collections.Immutable;

namespace TerseSharp.Core;

public readonly record struct TestFailure(string Name, string Message, string? Frame, long DurationMs);

public readonly record struct TestTiming(string Name, long DurationMs);

public readonly record struct TestProjectSummary(string Project, int Passed, int Failed, int Skipped, int Total, long DurationMs);

public readonly record struct TestRunReport(
    int Passed,
    int Failed,
    int Skipped,
    int Total,
    long DurationMs,
    ImmutableArray<TestFailure> Failures,
    ImmutableArray<TestTiming> PassedTests)
{
    public static TestRunReport Empty { get; } = new(0, 0, 0, 0, 0, [], []);

    public ImmutableArray<TestProjectSummary> Projects { get; init; } = [];

    public TestRunReport Merge(TestRunReport other) => new(
        Passed + other.Passed,
        Failed + other.Failed,
        Skipped + other.Skipped,
        Total + other.Total,
        DurationMs + other.DurationMs,
        Failures.AddRange(other.Failures),
        PassedTests.AddRange(other.PassedTests))
    {
        Projects = Listed(Projects).AddRange(Listed(other.Projects)),
    };

    public TestRunReport Sorted() => this with
    {
        Failures = [.. Failures.OrderBy(failure => failure.Name, StringComparer.Ordinal)],
        PassedTests = [.. PassedTests.OrderBy(test => test.Name, StringComparer.Ordinal)],
        Projects = [.. Listed(Projects).OrderBy(project => project.Project, StringComparer.Ordinal)],
    };

    public IEnumerable<TestTiming> Slowest(int count) => PassedTests
        .Concat(Failures.Select(failure => new TestTiming(failure.Name, failure.DurationMs)))
        .OrderByDescending(test => test.DurationMs)
        .ThenBy(test => test.Name, StringComparer.Ordinal)
        .Take(count);

    private static ImmutableArray<TestProjectSummary> Listed(ImmutableArray<TestProjectSummary> projects) =>
        projects.IsDefault ? [] : projects;
}
