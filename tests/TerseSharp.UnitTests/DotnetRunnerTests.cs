using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class DotnetRunnerTests
{
    [Fact]
    public void RenderBuild_WithAWorkspaceRoot_StripsItFromEveryDiagnosticAndFromTheOutputTail()
    {
        var root = Path.Combine("C:", "repo");
        var absolute = Path.Combine(root, "src", "A.cs") + "(7,9): error CS0029: cannot convert [" + Path.Combine(root, "src", "A.csproj") + "]";

        var text = DotnetRunner.RenderBuild("A.slnx", root, Failed(absolute), verbose: false);

        Assert.Contains(Path.Combine("src", "A.cs") + "(7,9): error CS0029", text, StringComparison.Ordinal);
        Assert.Contains("[" + Path.Combine("src", "A.csproj") + "]", text, StringComparison.Ordinal);
        Assert.DoesNotContain(root, text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBuild_WithoutAWorkspaceRoot_LeavesTheDiagnosticUntouched()
    {
        var text = DotnetRunner.RenderBuild("A.slnx", string.Empty, Failed(ErrorLine), verbose: false);

        Assert.Contains(ErrorLine, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("error MSB3021: Unable to copy file")]
    [InlineData("warning MSB3027: Could not copy")]
    [InlineData("The process cannot access the file because it is being used by another process.")]
    [InlineData("BEING USED BY ANOTHER PROCESS")]
    public void IsLockedOutput_ForALockSignatureOnAFailedBuild_IsTrue(string output) =>
        Assert.True(DotnetRunner.IsLockedOutput(1, output));

    [Fact]
    public void IsLockedOutput_ForALockSignatureOnASuccessfulBuild_IsFalse() =>
        Assert.False(DotnetRunner.IsLockedOutput(0, "warning MSB3026: being used by another process"));

    [Theory]
    [InlineData("error CS1002: ; expected")]
    [InlineData("")]
    [InlineData("Build succeeded.")]
    public void IsLockedOutput_ForAnOrdinaryFailure_IsFalse(string output) =>
        Assert.False(DotnetRunner.IsLockedOutput(1, output));

    [Fact]
    public void Diagnostics_SeparatesErrorsFromWarnings()
    {
        var diagnostics = DotnetRunner.Diagnostics(
            "src/A.cs(3,5): warning CS0169: field is never used [A.csproj]\n" +
            "src/A.cs(7,9): error CS0029: cannot convert [A.csproj]\n" +
            "src/A.cs(9,1): warning CA1822: mark as static [A.csproj]\n");

        Assert.Equal(["src/A.cs(7,9): error CS0029: cannot convert [A.csproj]"], diagnostics.Errors);
        Assert.Equal(2, diagnostics.Warnings.Length);
    }

    [Fact]
    public void Diagnostics_ForTheSameLineRepeatedPerTargetFramework_KeepsOneCopy()
    {
        var line = "src/A.cs(3,5): warning CS0169: field is never used [A.csproj]";
        var diagnostics = DotnetRunner.Diagnostics(line + "\n" + line + "\n" + line + "\n");

        Assert.Empty(diagnostics.Errors);
        Assert.Equal([line], diagnostics.Warnings);
    }

    [Fact]
    public void Diagnostics_ForAWarningWhoseMessageMentionsAnError_StaysAWarning()
    {
        var diagnostics = DotnetRunner.Diagnostics(
            "src/A.cs(3,5): warning CS0618: Report: error handling is obsolete [A.csproj]\n");

        Assert.Empty(diagnostics.Errors);
        Assert.Single(diagnostics.Warnings);
    }

    private const string ErrorLine = "src/A.cs(7,9): error CS0029: cannot convert [A.csproj]";
    private const string WarningLine = "src/A.cs(3,5): warning CS0169: field is never used [A.csproj]";
    private const string SecondWarningLine = "src/A.cs(9,1): warning CA1822: mark as static [A.csproj]";

    [Fact]
    public void RenderBuild_WhenTheBuildSucceedsWithWarnings_StillAnswersInOneLine()
    {
        var text = DotnetRunner.RenderBuild("A.slnx", string.Empty, Succeeded(WarningLine, SecondWarningLine), verbose: false);

        Assert.Equal("build ok  errors=0 warnings=2 emitted  elapsedMs=120", text);
    }

    [Fact]
    public void RenderBuild_WhenTheBuildSucceedsWithWarningsAndVerboseIsAsked_ListsThem()
    {
        var text = DotnetRunner.RenderBuild("A.slnx", string.Empty, Succeeded(WarningLine, SecondWarningLine), verbose: true);

        Assert.Contains("2 diagnostics (truncated=false, total=2)", text, StringComparison.Ordinal);
        Assert.Contains(WarningLine, text, StringComparison.Ordinal);
        Assert.Contains(SecondWarningLine, text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBuild_WhenTheBuildFails_ListsTheErrorsAndCountsTheHiddenWarnings()
    {
        var text = DotnetRunner.RenderBuild("A.slnx", string.Empty, Failed(WarningLine, ErrorLine, SecondWarningLine), verbose: false);

        Assert.Contains(ErrorLine, text, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0169", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CA1822", text, StringComparison.Ordinal);
        Assert.Contains("warnings=2 hidden", text, StringComparison.Ordinal);
        Assert.Contains("1/3 diagnostics truncated", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBuild_WhenTheBuildFailsAndVerboseIsAsked_ListsTheWarningsToo()
    {
        var text = DotnetRunner.RenderBuild("A.slnx", string.Empty, Failed(WarningLine, ErrorLine, SecondWarningLine), verbose: true);

        Assert.Contains(ErrorLine, text, StringComparison.Ordinal);
        Assert.Contains(WarningLine, text, StringComparison.Ordinal);
        Assert.Contains(SecondWarningLine, text, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBuild_WhenTheBuildFailsWithNoErrorLine_ListsTheWarningsAndKeepsTheOutputTail()
    {
        var text = DotnetRunner.RenderBuild(
            "A.slnx",
            string.Empty,
            Failed(WarningLine, SecondWarningLine, "MSBUILD : Build FAILED for an unstated reason"),
            verbose: false);

        Assert.Contains(WarningLine, text, StringComparison.Ordinal);
        Assert.Contains(SecondWarningLine, text, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", text, StringComparison.Ordinal);
        Assert.Contains("MSBUILD : Build FAILED for an unstated reason", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBuild_WhenALockedOutputFileBlockedTheBuild_IsNeverCondensedToTheOneLineForm()
    {
        const string lockLine = "A.csproj(0,0): error MSB3021: Unable to copy file, it is being used by another process";

        var text = DotnetRunner.RenderBuild("A.slnx", string.Empty, Failed(lockLine, WarningLine), verbose: false);

        Assert.Contains("WARNING a locked output file blocked the operation", text, StringComparison.Ordinal);
        Assert.Contains(lockLine, text, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0169", text, StringComparison.Ordinal);
        Assert.Contains("warnings=1 hidden", text, StringComparison.Ordinal);
    }

    private static ProcessRun Succeeded(params string[] lines) => new(0, string.Join('\n', lines), 120);

    private static ProcessRun Failed(params string[] lines) => new(1, string.Join('\n', lines), 120);

    [Fact]
    public void RenderNoResults_WhenTheBuildInsideTheTestRunFailed_ListsTheErrorsWithoutTheWarnings()
    {
        var text = DotnetRunner.RenderNoResults("A.slnx", Failed(WarningLine, ErrorLine, SecondWarningLine), verbose: false);

        Assert.Contains("no test results were produced", text, StringComparison.Ordinal);
        Assert.Contains(ErrorLine, text, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0169", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CA1822", text, StringComparison.Ordinal);
        Assert.Contains("warnings=2 hidden", text, StringComparison.Ordinal);
        Assert.DoesNotContain("last output lines", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderNoResults_WhenVerboseIsAsked_ListsTheWarningsToo()
    {
        var text = DotnetRunner.RenderNoResults("A.slnx", Failed(WarningLine, ErrorLine, SecondWarningLine), verbose: true);

        Assert.Contains(ErrorLine, text, StringComparison.Ordinal);
        Assert.Contains(WarningLine, text, StringComparison.Ordinal);
        Assert.Contains(SecondWarningLine, text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderNoResults_WhenTheOutputHoldsNoDiagnosticAtAll_FallsBackToTheOutputTail()
    {
        var text = DotnetRunner.RenderNoResults("A.slnx", Failed("The active test run was aborted."), verbose: false);

        Assert.Contains("FAILED with no error-severity diagnostic; last output lines:", text, StringComparison.Ordinal);
        Assert.Contains("The active test run was aborted.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderNoResults_WhenTheFailureCarriesOnlyWarnings_StaysBoundedByTheOutputTail()
    {
        var text = DotnetRunner.RenderNoResults("A.slnx", Failed(ManyWarningsThenACrash()), verbose: false);

        Assert.Contains("The active test run was aborted.", text, StringComparison.Ordinal);
        Assert.Contains("FAILED with no error-severity diagnostic; last output lines:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", text, StringComparison.Ordinal);
        Assert.True(text.Split('\n').Length <= 18, text);
    }

    [Fact]
    public void RenderNoResults_WhenTheFailureCarriesOnlyWarningsAndVerboseIsAsked_StillShowsTheOutputTail()
    {
        var text = DotnetRunner.RenderNoResults("A.slnx", Failed(ManyWarningsThenACrash()), verbose: true);

        Assert.Contains("src/A0.cs(1,1): warning CS0169: field is never used [A.csproj]", text, StringComparison.Ordinal);
        Assert.Contains("src/A39.cs(1,1): warning CS0169: field is never used [A.csproj]", text, StringComparison.Ordinal);
        Assert.Contains("The active test run was aborted.", text, StringComparison.Ordinal);
    }

    private static string[] ManyWarningsThenACrash() =>
    [
        .. Enumerable.Range(0, 40).Select(index =>
            FormattableString.Invariant($"src/A{index}.cs(1,1): warning CS0169: field is never used [A.csproj]")),
        "Testhost process exited with error. The active test run was aborted.",
    ];

    [Fact]
    public void RenderTestNames_WhenTheListingSucceededButMatchedNothing_SaysNothingElse()
    {
        var text = DotnetRunner.RenderTestNames("A.slnx", Succeeded(WarningLine, SecondWarningLine), contains: "NoSuchTest");

        Assert.Equal("0 tests", text);
    }

    [Fact]
    public void RenderTestNames_WhenTheBuildFailed_ListsTheErrorsWithoutTheWarnings()
    {
        var text = DotnetRunner.RenderTestNames("A.slnx", Failed(WarningLine, ErrorLine), contains: null);

        Assert.Contains(ErrorLine, text, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0169", text, StringComparison.Ordinal);
        Assert.Contains("warnings=1 hidden", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PerProject_OnASingleProjectRun_AddsNothingToTheGreenOneLiner()
    {
        var report = TerseSharp.Core.TestRunReport.Empty with
        {
            Projects = [new TerseSharp.Core.TestProjectSummary("Only.Tests", 3, 0, 0, 3, 12)],
        };

        Assert.Equal(string.Empty, DotnetRunner.PerProject(report));
    }

    [Fact]
    public void PerProject_OnAMultiProjectRun_NamesEveryProjectWithItsOwnCountAndDuration()
    {
        var report = TerseSharp.Core.TestRunReport.Empty with
        {
            Projects =
            [
                new TerseSharp.Core.TestProjectSummary("TerseSharp.UnitTests", 310, 0, 0, 310, 12043),
            new TerseSharp.Core.TestProjectSummary("TerseSharp.E2ETests", 168, 0, 0, 168, 110328),
        ],
        };

        Assert.Equal("  TerseSharp.UnitTests:310/12043ms  TerseSharp.E2ETests:168/110328ms", DotnetRunner.PerProject(report));
    }

    [Fact]
    public void RenderNoResults_WithTheWorkspaceRoot_RelativisesTheOutputTailInsteadOfEchoingAbsolutePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "repo");
        var absolute = Path.Combine(root, "tests", "OrderTests.cs");

        var text = DotnetRunner.RenderNoResults("A.slnx", Failed("crashed while running " + absolute), verbose: false, root);

        Assert.Contains(Path.Combine("tests", "OrderTests.cs"), text, StringComparison.Ordinal);
        Assert.DoesNotContain(root, text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderNoResults_WithNoRoot_LeavesTheOutputTailAlone()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "repo", "tests", "OrderTests.cs");

        var text = DotnetRunner.RenderNoResults("A.slnx", Failed("crashed while running " + absolute), verbose: false);

        Assert.Contains(absolute, text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderTestNames_WithTheWorkspaceRoot_RelativisesTheFailureOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "repo");
        var absolute = Path.Combine(root, "src", "Trading.csproj");

        var text = DotnetRunner.RenderTestNames("A.slnx", Failed("MSB1009: " + absolute), contains: null, root);

        Assert.DoesNotContain(root, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Invocations_WithNoTargetsSelected_FallsBackToTheSingleTarget() =>
        Assert.Equal(["A.slnx"], Request("A.slnx").Invocations);


    [Fact]
    public void Invocations_WithTargetsSelected_RunsExactlyThoseProjects() =>
        Assert.Equal(["one.csproj", "two.csproj"], (Request("A.slnx") with { Targets = ["one.csproj", "two.csproj"] }).Invocations);

    [Fact]
    public void Merge_KeepsTheFirstNonZeroExitCode_SoAFailedProjectIsNeverReportedAsGreen()
    {
        var merged = DotnetRunner.Merge(new ProcessRun(0, "a", 10), new ProcessRun(3, "b", 20));

        Assert.Equal(3, merged.ExitCode);
    }

    [Fact]
    public void Merge_WhenTheFirstProjectFailed_DoesNotLetALaterGreenProjectClearIt()
    {
        var merged = DotnetRunner.Merge(new ProcessRun(3, "a", 10), new ProcessRun(0, "b", 20));

        Assert.Equal(3, merged.ExitCode);
    }

    [Fact]
    public void Merge_SumsElapsedAndOrsTheTimeoutAndKeepsBothOutputs()
    {
        var merged = DotnetRunner.Merge(new ProcessRun(0, "a", 10), new ProcessRun(0, "b", 20, TimedOut: true));

        Assert.Equal(30, merged.ElapsedMilliseconds);
        Assert.True(merged.TimedOut);
        Assert.Contains("a", merged.Output, StringComparison.Ordinal);
        Assert.Contains("b", merged.Output, StringComparison.Ordinal);
    }

    private static TestRunRequest Request(string target) =>
        new(target, null, false, false, 0, TimeSpan.FromSeconds(60));

    [Fact]
    public void OutputNotes_ForTheServersOwnAssembly_NamesTheProbeCommandForTheBinaryJustBuilt()
    {
        var output = string.Join(
            '\n',
            "  TerseSharp.Core -> /repo/src/TerseSharp.Core/bin/Debug/net10.0/TerseSharp.Core.dll",
            "  TerseSharp.Server -> /repo/src/TerseSharp.Server/bin/Debug/net10.0/terse.dll");

        var notes = DotnetRunner.OutputNotes(output, "/repo", "/repo/TerseSharp.slnx").Select(Slashed).ToArray();

        Assert.Equal(3, notes.Length);
        Assert.Equal("wrote src/TerseSharp.Core/bin/Debug/net10.0/TerseSharp.Core.dll", notes[0]);
        Assert.Equal("wrote src/TerseSharp.Server/bin/Debug/net10.0/terse.dll", notes[1]);
        Assert.StartsWith(
            "probe: dotnet \"/repo/src/TerseSharp.Server/bin/Debug/net10.0/terse.dll\" call <tool> --workspace \"/repo/TerseSharp.slnx\" --json ",
            notes[2],
            StringComparison.Ordinal);
    }

    private static string Slashed(string note) => note.Replace('\\', '/');

    [Fact]
    public void OutputNotes_ForABuildThatWroteNothingElse_NamesNoProbeForAnotherAssembly()
    {
        var notes = DotnetRunner.OutputNotes("  Fixture.Trading -> /repo/bin/Fixture.Trading.dll", "/repo", "/repo/FixtureSolution.slnx");

        Assert.Equal(["wrote bin/Fixture.Trading.dll"], notes.Select(Slashed));
    }

    [Fact]
    public void Unfinished_UnderAConcurrentBatch_NamesOnlyTheProjectsThatTimedOut() =>
        Assert.Equal(
            ["B.Tests"],
            DotnetRunner.Unfinished(
                ["a/A.Tests.csproj", "b/B.Tests.csproj", "c/C.Tests.csproj"],
                [Finished, TimedOutRun, Finished]));


    [Fact]
    public void Unfinished_UnderASerialBatchThatStopped_NamesTheTimedOutProjectAndEveryProjectItNeverStarted() =>
        Assert.Equal(
            ["A.Tests", "B.Tests", "C.Tests"],
            DotnetRunner.Unfinished(
                ["a/A.Tests.csproj", "b/B.Tests.csproj", "c/C.Tests.csproj"],
                [TimedOutRun, null, null]));


    [Fact]
    public void Stopped_ForAConcurrentBatch_SaysTheRestOfTheBatchStillRan() =>
        Assert.Equal(
            "1 of 3 project(s) timed out; the rest of the batch still ran",
            DotnetRunner.Stopped(["B.Tests"], 3, serial: false));


    [Fact]
    public void Stopped_ForASerialBatch_SaysItStoppedAtTheFirstTimeout() =>
        Assert.Equal(
            "the batch stopped at the first timeout; 3 of 3 project(s) produced no results",
            DotnetRunner.Stopped(["A.Tests", "B.Tests", "C.Tests"], 3, serial: true));


    [Fact]
    public void Stopped_ForASingleProject_NeverMentionsABatch() =>
        Assert.Equal(
            "this run timed out and produced no results",
            DotnetRunner.Stopped(["A.Tests"], 1, serial: false));

    private static ProcessRun Finished => new(0, string.Empty, 10);

    private static ProcessRun TimedOutRun => new(-1, string.Empty, 10, TimedOut: true);
}
