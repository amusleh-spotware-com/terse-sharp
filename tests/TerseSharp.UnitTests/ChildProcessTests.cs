using System.Collections;
using System.Globalization;
using TerseSharp.Core;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ChildProcessTests
{
    private static readonly string[] LocatorVariables =
        ["MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath"];

    [Fact]
    public void StartInfo_ForEveryVariableTheLocatorRegistered_KeepsItOutOfTheChild()
    {
        var registered = Registered();

        var start = ChildProcess.StartInfo("dotnet", ["--version"], AppContext.BaseDirectory);

        Assert.True(registered.Length > 0, "MSBuildLocator registered none of its variables, so this test proves nothing");
        Assert.All(registered, variable => Assert.False(start.Environment.ContainsKey(variable)));
    }

    [Fact]
    public void StartInfo_ForEveryOtherVariable_LeavesItInherited()
    {
        var inherited = Environment.GetEnvironmentVariables()
            .Keys
            .Cast<string>()
            .Where(name => !LocatorVariables.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Where(name => !MutatedByOtherTests.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var start = ChildProcess.StartInfo("dotnet", [], AppContext.BaseDirectory);
        var stillSet = inherited.Where(name => Environment.GetEnvironmentVariable(name) is not null).ToArray();

        Assert.True(stillSet.Length > 0, "this process carries no environment, so the test proves nothing");
        Assert.All(stillSet, name => Assert.True(start.Environment.ContainsKey(name), name + " was dropped from the child's environment"));
    }

    [Fact]
    public void StartInfo_PassesEveryArgumentThroughTheArgumentList()
    {
        var start = ChildProcess.StartInfo("dotnet", ["build", "-p:Name=Value"], AppContext.BaseDirectory);

        Assert.Equal(["build", "-p:Name=Value"], start.ArgumentList);
        Assert.False(start.UseShellExecute);
    }

    [Fact]
    public void RegisteredMsBuildVariables_CoversEveryMsBuildVariableThatPointsAtADirectory()
    {
        var registered = Registered();
        var uncovered = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Where(entry => ((string)entry.Key).StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase))
            .Where(entry => entry.Value is string value && value.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(entry => (string)entry.Key)
            .Where(name => !LocatorVariables.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(registered.Length > 0, "MSBuildLocator registered none of its variables, so this test proves nothing");
        Assert.True(
            uncovered.Length is 0,
            "these MSBuild variables name a directory and reach every child unscrubbed: " + string.Join(", ", uncovered));
    }

    private static string[] Registered()
    {
        MsBuildBootstrap.Ensure();

        return [.. LocatorVariables.Where(variable => Environment.GetEnvironmentVariable(variable) is { Length: > 0 })];
    }

    private const string ChildMarker = "terse-child-marker";

    private static string BlockingShell => OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";

    private static string[] BlockingEcho => OperatingSystem.IsWindows()
        ? ["/c", "echo " + ChildMarker + " & ping -n 60 127.0.0.1"]
        : ["-c", "echo " + ChildMarker + "; sleep 60"];

    [Fact]
    public async Task RunAsync_WhenTheDeadlineExpires_KeepsWhatTheChildAlreadyWrote()
    {
        var run = await ChildProcess.RunAsync(
            BlockingShell,
            BlockingEcho,
            AppContext.BaseDirectory,
            TimeSpan.FromSeconds(15),
            TestContext.Current.CancellationToken);

        Assert.True(run.TimedOut, "the child outlives the deadline, so the run must report a timeout");
        Assert.Contains(ChildMarker, run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains(ChildMarker, run.Output, StringComparison.Ordinal);
        Assert.Contains("TIMED_OUT after", run.Output, StringComparison.Ordinal);
    }

    private static string[] DetachedHolder => OperatingSystem.IsWindows()
        ? ["/c", "start /b ping -n 60 127.0.0.1 & echo " + ChildMarker]
        : ["-c", "sleep 60 & echo " + ChildMarker];

    [Fact]
    public async Task RunAsync_WhenADetachedGrandchildKeepsThePipeOpen_AnswersAndSaysTheCaptureIsIncomplete()
    {
        var deadline = TimeSpan.FromSeconds(45);

        var run = await ChildProcess.RunAsync(
            BlockingShell,
            DetachedHolder,
            AppContext.BaseDirectory,
            deadline,
            TestContext.Current.CancellationToken);

        Assert.False(run.TimedOut, "the child exited, so only the leaked pipe holder could have kept the run waiting");
        Assert.True(run.ElapsedMilliseconds < deadline.TotalMilliseconds, "the run waited for the deadline instead of the child");
        Assert.Contains(ChildMarker, run.Output, StringComparison.Ordinal);
        Assert.False(run.Drained, "the holder still owns the stream, so the captured text is a partial snapshot and must say so");
    }

    [Fact]
    public async Task RunAsync_WhenTheCallerCancels_DoesNotReportItAsADeadlineTimeout()
    {
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var run = await ChildProcess.RunAsync(
            BlockingShell,
            BlockingEcho,
            AppContext.BaseDirectory,
            TimeSpan.FromMinutes(5),
            caller.Token);

        Assert.False(run.TimedOut, "the deadline still had minutes left, so calling this a timeout would send the agent to raise timeoutSeconds");
        Assert.Contains("CANCELLED after", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("TIMED_OUT", run.Output, StringComparison.Ordinal);
    }

    private static readonly string[] MutatedByOtherTests = ["TERSE_HOME", "CLAUDE_CONFIG_DIR"];

    [Fact]
    public async Task RunAsync_WhenADetachedGrandchildKeepsThePipeOpen_StillCapturesWhatTheChildAlreadyWroteWithoutASecondFullGrace()
    {
        var deadline = TimeSpan.FromSeconds(45);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var run = await ChildProcess.RunAsync(
            BlockingShell,
            DetachedHolder,
            AppContext.BaseDirectory,
            deadline,
            TestContext.Current.CancellationToken);

        clock.Stop();

        Assert.False(run.Drained);
        Assert.NotEqual(0, run.Output.Length);
        Assert.Contains(ChildMarker, run.StandardOutput, StringComparison.Ordinal);
        Assert.True(clock.ElapsedMilliseconds < 10_000, string.Create(CultureInfo.InvariantCulture, $"the settle added {clock.ElapsedMilliseconds} ms on top of the 2 s drain grace"));
    }
}
