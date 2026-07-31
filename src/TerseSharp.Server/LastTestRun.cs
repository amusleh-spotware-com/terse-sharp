using System.Collections.Immutable;

namespace TerseSharp.Server;

public sealed record TestRunMemory(string WorkspaceRoot, string Target, ImmutableArray<string> FailedTests)
{
    public bool Covers(string workspaceRoot) =>
        WorkspaceRoot.Equals(workspaceRoot, PathBoundary.Comparison) && !FailedTests.IsDefaultOrEmpty;
}

public sealed class LastTestRun
{
    private const int MaxRememberedTests = 200;

    private TestRunMemory memory = new(string.Empty, string.Empty, []);

    public TestRunMemory Memory => Volatile.Read(ref memory);

    public void Remember(string workspaceRoot, string target, IEnumerable<string> failedTests) =>
        Volatile.Write(ref memory, new TestRunMemory(workspaceRoot, target, [.. failedTests.Take(MaxRememberedTests)]));
}
