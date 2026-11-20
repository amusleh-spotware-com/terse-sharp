namespace TerseSharp.Server;

internal static class HostExit
{
    private const string AbortMarker = "aborted. Reason:";

    internal static bool Early(ProcessRun run, TimeSpan timeout, string[] hung) =>
        hung.Length is not 0 && !run.TimedOut && BeforeHangWindow(run, timeout);

    internal static string Render(string response, List<string> missing, string[] hung, ProcessRun run) => string.Create(
        CultureInfo.InvariantCulture,
        $"{response}\nWARNING the test host exited early (exitCode={run.ExitCode}, elapsedMs={run.ElapsedMilliseconds}), before any deadline - this is not a timeout - while these test(s) were still running: {string.Join(", ", hung)}{Incomplete(missing)}{Reason(run.Output)}\nremedy: run each named test alone with run_tests test=\"<name>\"; one that passes alone was ended by something outside its own code");

    private static string Incomplete(List<string> missing) => missing.Count is 0
        ? string.Empty
        : string.Create(CultureInfo.InvariantCulture, $"\nincomplete project(s): {string.Join(", ", missing)}");

    internal static string Reason(string output)
    {
        foreach (var line in output.AsSpan().EnumerateLines())
        {
            var at = line.IndexOf(AbortMarker, StringComparison.Ordinal);

            if (at >= 0)
                return string.Concat("\nreason: ", line[(at + AbortMarker.Length)..].Trim());
        }

        return string.Empty;
    }

    private static bool BeforeHangWindow(ProcessRun run, TimeSpan timeout) =>
            DotnetRunner.HangWindow(timeout) is not { } window || run.ElapsedMilliseconds < (long)window.TotalMilliseconds;
}
