namespace TerseSharp.Server.Tools;

internal static class UnbuiltReplay
{
    internal const string Note = "NOTE no failure is remembered - the last run_tests in this workspace failed to build, so its whole selection was replayed instead";

    internal static TestRunRequest Request(TestRunMemory memory, TimeSpan timeout, bool verbose, BuildScope requested) => new(
        memory.Target,
        memory.Unbuilt?.Filter,
        false,
        false,
        0,
        timeout,
        verbose,
        BuildTools.Remembered(memory.Scope, requested),
        Targets: memory.Unbuilt?.Targets ?? default);

    internal static string? Refused(string?[]? tests, string?[]? exclude, bool noBuild) => (tests is { Length: > 0 } || exclude is { Length: > 0 }, noBuild) switch
    {
        (true, _) => Errors.Invalid(
            "tests= and exclude= filter remembered failures, and none is remembered - the last run_tests failed to build, so only its whole selection can be replayed",
            "call rerun_failed without tests= and exclude=, or run_tests with the tests you want").Render(),
        (_, true) => Errors.Invalid(
            "noBuild=true would replay the binary from before the build that failed, so a green answer would be a false one",
            "call rerun_failed without noBuild=true, so the replay rebuilds first").Render(),
        _ => null,
    };
}
