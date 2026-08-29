using System.Collections.Immutable;
using TerseSharp.Core;
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
    public void Unfinished_UnderAConcurrentBatch_NamesOnlyTheProjectsThatTimedOut()
    {
        var results = Slots(3, [0, 2]);

        try
        {
            Assert.Equal(
                ["B.Tests"],
                DotnetRunner.Unfinished(
                    ["a/A.Tests.csproj", "b/B.Tests.csproj", "c/C.Tests.csproj"],
                    [Finished, TimedOutRun, Finished],
                    results.FullName));
        }
        finally
        {
            results.Delete(recursive: true);
        }
    }

    [Fact]
    public void Unfinished_UnderASerialBatchThatStopped_NamesTheTimedOutProjectAndEveryProjectItNeverStarted()
    {
        var results = Slots(3, []);

        try
        {
            Assert.Equal(
                ["A.Tests", "B.Tests", "C.Tests"],
                DotnetRunner.Unfinished(
                    ["a/A.Tests.csproj", "b/B.Tests.csproj", "c/C.Tests.csproj"],
                    [TimedOutRun, null, null],
                    results.FullName));
        }
        finally
        {
            results.Delete(recursive: true);
        }
    }

    [Fact]
    public void Stopped_ForAConcurrentBatch_SaysTheRestOfTheBatchStillRan() =>
        Assert.Equal(
            "1 of 3 project(s) timed out; the rest of the batch still ran",
            DotnetRunner.Stopped(["B.Tests"], 3, serial: false, timedOut: true));

    [Fact]
    public void Stopped_ForASerialBatch_SaysItStoppedAtTheFirstTimeout() =>
        Assert.Equal(
            "the batch stopped at the first project that timed out; 3 of 3 project(s) produced no results",
            DotnetRunner.Stopped(["A.Tests", "B.Tests", "C.Tests"], 3, serial: true, timedOut: true));

    [Fact]
    public void Stopped_ForASingleProject_NeverMentionsABatch() =>
        Assert.Equal(
            "this run timed out and produced no results",
            DotnetRunner.Stopped(["A.Tests"], 1, serial: false, timedOut: true));

    private static ProcessRun Finished => new(0, string.Empty, 10);

    private static ProcessRun TimedOutRun => new(-1, string.Empty, 10, TimedOut: true);

    [Fact]
    public void Stopped_ForAConcurrentBatchWhereEveryProjectTimedOut_NeverClaimsTheRestStillRan() =>
        Assert.Equal(
            "every project of the batch timed out; all 3 produced no results",
            DotnetRunner.Stopped(["A.Tests", "B.Tests", "C.Tests"], 3, serial: false, timedOut: true));

    private static DirectoryInfo Slots(int count, int[] produced)
    {
        var results = Directory.CreateTempSubdirectory("terse-slots-");

        for (var index = 0; index < count; index++)
        {
            var slot = results.CreateSubdirectory(index.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (Array.IndexOf(produced, index) >= 0)
                File.WriteAllText(Path.Combine(slot.FullName, "results.trx"), "<TestRun />");
        }

        return results;
    }

    [Fact]
    public void Unfinished_WhenABlameAbortLeftAPartialTrxBesideItsSequenceFile_StillCountsThatProjectAsUnfinished()
    {
        var results = Slots(2, [0, 1]);

        try
        {
            File.WriteAllText(Path.Combine(results.FullName, "0", "run_Sequence.xml"), "<TestSequence />");

            Assert.Equal(
                ["A.Tests"],
                DotnetRunner.Unfinished(["a/A.Tests.csproj", "b/B.Tests.csproj"], [Finished, Finished], results.FullName));
        }
        finally
        {
            results.Delete(recursive: true);
        }
    }

    [Fact]
    public void RenderNoResults_ForATimedOutRunWhoseTailHoldsALockSignature_DoesNotClaimALockedOutputBlockedIt()
    {
        var run = new ProcessRun(
            -1,
            "MSB3021: Unable to copy file - it is being used by another process\nTIMED_OUT after 600000 ms; the process tree was killed",
            600000,
            TimedOut: true,
            StandardOutput: "MSB3021: Unable to copy file - it is being used by another process",
            Stopped: true);

        var text = DotnetRunner.RenderNoResults("A.slnx", run, verbose: false);

        Assert.Contains("FAILED timed out after", text, StringComparison.Ordinal);
        Assert.Contains("MSB3021", text, StringComparison.Ordinal);
        Assert.DoesNotContain("a locked output file blocked the operation", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_WhenOneProjectsCaptureWasIncomplete_NeverLetsTheBatchClaimAWholeOne()
    {
        var whole = new ProcessRun(0, "first", 10, StandardOutput: "first");
        var partial = new ProcessRun(0, "second", 20, StandardOutput: "second", Drained: false);

        Assert.False(DotnetRunner.Merge(whole, partial).Drained);
        Assert.False(DotnetRunner.Merge(partial, whole).Drained);
        Assert.True(DotnetRunner.Merge(whole, whole).Drained);
    }

    [Fact]
    public void RenderNoResults_ForACancelledRunWhoseTailHoldsALockSignature_DoesNotClaimALockedOutputBlockedIt()
    {
        var run = new ProcessRun(
            -1,
            "MSB3021: Unable to copy file - it is being used by another process\nCANCELLED after 8123 ms; the process tree was killed",
            8123,
            StandardOutput: "MSB3021: Unable to copy file - it is being used by another process",
            Stopped: true);

        var text = DotnetRunner.RenderNoResults("A.slnx", run, verbose: false);

        Assert.Contains("MSB3021", text, StringComparison.Ordinal);
        Assert.DoesNotContain("a locked output file blocked the operation", text, StringComparison.Ordinal);
        Assert.DoesNotContain("raise timeoutSeconds", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_WhenOneProjectWasStopped_KeepsThatFactSoTheBatchIsNeverReadAsALockedRun()
    {
        var finished = new ProcessRun(0, "first", 10, StandardOutput: "first");
        var killed = new ProcessRun(-1, "second", 20, StandardOutput: "second", Stopped: true);

        Assert.True(DotnetRunner.Merge(finished, killed).Stopped);
        Assert.True(DotnetRunner.Merge(killed, finished).Stopped);
        Assert.False(DotnetRunner.Merge(finished, finished).Stopped);
    }

    [Fact]
    public void RenderTestNames_ForAStoppedRunThatHadAlreadyPrintedSomeNames_NeverPassesThemOffAsTheWholeSuite()
    {
        var run = new ProcessRun(
            -1,
            "    Fixture.Trading.Tests.OrderTests.Submits\n    Fixture.Trading.Tests.OrderTests.Rejects\nTIMED_OUT after 600000 ms; the process tree was killed",
            600000,
            TimedOut: true,
            Stopped: true);

        var text = DotnetRunner.RenderTestNames("A.slnx", run, contains: null);

        Assert.Contains("Fixture.Trading.Tests.OrderTests.Submits", text, StringComparison.Ordinal);
        Assert.Contains("this listing is partial", text, StringComparison.Ordinal);
        Assert.Contains("raise timeoutSeconds", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderTest_ForATimedOutBatchThatAlreadyHasResults_StillCarriesTheDeadlineRemedy()
    {
        var report = new TestRunReport(3, 0, 0, 3, 900, [], []);
        var request = new TestRunRequest("A.slnx", null, false, false, 0, TimeSpan.FromSeconds(600));
        var run = new ProcessRun(-1, "TIMED_OUT after 600000 ms; the process tree was killed", 600000, TimedOut: true, Stopped: true);

        var text = DotnetRunner.RenderTest(run, report, request, root: string.Empty);

        Assert.Contains("the results below are partial", text, StringComparison.Ordinal);
        Assert.Contains("raise timeoutSeconds", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Stopped_WhenNothingTimedOut_NeverClaimsATimeout() =>
        Assert.Equal(
            "this run produced no results",
            DotnetRunner.Stopped(["A.Tests"], 1, serial: false, timedOut: false));

    [Fact]
    public void Stopped_ForABatchWhereNothingTimedOut_NamesTheMissingResultsRatherThanATimeout() =>
        Assert.Equal(
            "1 of 3 project(s) produced no results; the rest of the batch still ran",
            DotnetRunner.Stopped(["B.Tests"], 3, serial: false, timedOut: false));

    [Fact]
    public void RenderBuild_ForAFailureCarryingNoErrorSeverityDiagnostic_StillEchoesTheCommand()
    {
        var run = new ProcessRun(1, "MSBUILD : the build host exited", 120, Command: "dotnet build TerseSharp.slnx");

        var text = DotnetRunner.RenderBuild("TerseSharp.slnx", "C:/repo", run, verbose: false);

        Assert.Contains("command: dotnet build TerseSharp.slnx", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBuild_ForACleanBuild_StaysOneLineAndEchoesNoCommand()
    {
        var run = new ProcessRun(0, "Build succeeded.", 120, Command: "dotnet build TerseSharp.slnx");

        var text = DotnetRunner.RenderBuild("TerseSharp.slnx", "C:/repo", run, verbose: false);

        Assert.StartsWith("build ok", text, StringComparison.Ordinal);
        Assert.DoesNotContain("command:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderNoResults_WhenTheElapsedReachedTheDeadlineWindow_CarriesTheRemedyWithoutTheProcessFlag()
    {
        var run = new ProcessRun(3, "Aborting test run: exceeded --timeout", 585_000, Command: "dotnet test --solution x");

        var text = DotnetRunner.RenderNoResults("x", run, verbose: false, root: "", deadline: TimeSpan.FromSeconds(600));

        Assert.False(run.TimedOut);
        Assert.Contains("raise timeoutSeconds", text, StringComparison.Ordinal);
        Assert.Contains("command: dotnet test --solution x", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderNoResults_ForAFastFailure_CarriesNoDeadlineRemedy()
    {
        var run = new ProcessRun(1, "boom", 900, Command: "dotnet test x");

        var text = DotnetRunner.RenderNoResults("x", run, verbose: false, root: "", deadline: TimeSpan.FromSeconds(600));

        Assert.DoesNotContain("raise timeoutSeconds", text, StringComparison.Ordinal);
        Assert.Contains("command: dotnet test x", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderTest_ForAGreenRunSpanningTwoProjects_ReportsWallClockAndConcurrency()
    {
        var report = new TestRunReport(10, 0, 0, 10, 8000, [], [])
        {
            Projects = [new("A", 5, 0, 0, 5, 4000), new("B", 5, 0, 0, 5, 4000)],
        };

        var text = DotnetRunner.RenderTest(
            new ProcessRun(0, string.Empty, 2000),
            report,
            new TestRunRequest("sln", null, false, false, 0, TimeSpan.FromSeconds(600)),
            "C:/repo");

        Assert.StartsWith("run_tests PASSED", text, StringComparison.Ordinal);
        Assert.Contains("elapsedMs=2000", text, StringComparison.Ordinal);
        Assert.Contains("concurrency=4.0x", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderTest_ForASingleProjectGreenRun_CarriesNeitherConcurrencyNorTheSlowestTest()
    {
        var text = DotnetRunner.RenderTest(
            new ProcessRun(0, string.Empty, 3600),
            new TestRunReport(1, 0, 0, 1, 18, [], []),
            new TestRunRequest("proj", null, false, false, 0, TimeSpan.FromSeconds(600)),
            "C:/repo");

        Assert.DoesNotContain("concurrency=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("slowestTest=", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderTest_ForARunUnderTwoTimesConcurrency_NamesTheSlowestTestThatHeldIt()
    {
        var report = new TestRunReport(1, 1, 0, 2, 5200, [new("A.Fails", "boom", null, 10)], [new("A.Slow", 4000)])
        {
            Projects = [new("A", 1, 1, 0, 2, 4200), new("B", 0, 0, 0, 0, 1000)],
        };

        var text = DotnetRunner.RenderTest(
            new ProcessRun(1, string.Empty, 4000),
            report,
            new TestRunRequest("sln", null, false, false, 0, TimeSpan.FromSeconds(600)),
            "C:/repo");

        Assert.Contains("concurrency=1.3x", text, StringComparison.Ordinal);
        Assert.Contains("slowestTest=A.Slow 4000ms", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_KeepsBothCommands_SoAConcurrentBatchStillReportsEveryInvocationItRan()
    {
        var first = new ProcessRun(0, "a", 10, Command: "dotnet test one.csproj");
        var next = new ProcessRun(0, "b", 20, Command: "dotnet test two.csproj");

        Assert.Equal("dotnet test one.csproj && dotnet test two.csproj", DotnetRunner.Merge(first, next).Command);
    }

    [Fact]
    public void Merge_WhenOneRunCarriesNoCommand_KeepsTheOtherWithoutADanglingSeparator()
    {
        var known = new ProcessRun(0, "a", 10, Command: "dotnet test one.csproj");
        var blank = new ProcessRun(0, "b", 20);

        Assert.Equal("dotnet test one.csproj", DotnetRunner.Merge(known, blank).Command);
        Assert.Equal("dotnet test one.csproj", DotnetRunner.Merge(blank, known).Command);
        Assert.Equal(string.Empty, DotnetRunner.Merge(blank, blank).Command);
    }

    [Fact]
    public void Expandable_OnAPlainSolutionRun_IsTrueSoItsTestProjectsRunAsSeparateConcurrentInvocations()
    {
        var solution = new TestRunRequest("A.slnx", null, false, false, 0, TimeSpan.FromSeconds(600));
        ImmutableArray<string> two = ["one.csproj", "two.csproj"];

        Assert.True(DotnetRunner.Expandable(solution, two));
        Assert.False(DotnetRunner.Expandable(solution, ["only.csproj"]));
        Assert.False(DotnetRunner.Expandable(solution, default));
        Assert.False(DotnetRunner.Expandable(solution with { Parallel = 1 }, two));
        Assert.False(DotnetRunner.Expandable(solution with { Filter = "FullyQualifiedName~Adder" }, two));
        Assert.False(DotnetRunner.Expandable(solution with { Targets = two }, two));
        Assert.False(DotnetRunner.Expandable(solution with { Target = "one.csproj" }, two));
        Assert.False(DotnetRunner.Expandable(solution with { Scope = new BuildScope("Release", null, default) }, two));
        Assert.False(DotnetRunner.Expandable(solution with { Reporter = TestReporter.TestingPlatformTrx }, two));
        Assert.False(DotnetRunner.Expandable(solution with { RunSettings = ["xUnit.MaxParallelThreads=1"] }, two));
    }

    [Fact]
    public void Builds_WhenASolutionWasExpanded_IsThatOneSolutionRatherThanEveryProjectItExpandedTo()
    {
        var request = new TestRunRequest("A.slnx", null, false, false, 0, TimeSpan.FromSeconds(600))
        {
            Targets = ["one.csproj", "two.csproj"],
            BuildTarget = "A.slnx",
        };

        Assert.Equal(["A.slnx"], request.Builds);
        Assert.Equal(["one.csproj", "two.csproj"], request.Invocations);
        Assert.Equal(["one.csproj", "two.csproj"], (request with { BuildTarget = null }).Builds);
    }

    [Fact]
    public void Arguments_ForADirectExpansion_RunTheBuiltTestAssemblyItselfWithATrxUnderTheSlot()
    {
        var request = new TestRunRequest("C:/repo/bin/Unit.Tests.dll", null, true, false, 0, TimeSpan.FromSeconds(600), Direct: true);

        Assert.Equal(
            ["C:/repo/bin/Unit.Tests.dll", "-trx", Path.Combine("C:/slot", "results.trx"), "-noLogo", "-reporter", "quiet"],
            DotnetRunner.Arguments(request, "C:/slot"));
    }

    [Fact]
    public void Arguments_WhenTheRunIsNotADirectExpansion_StaysOnDotnetTestEvenForAnAssemblyPath()
    {
        var request = new TestRunRequest("C:/repo/bin/Unit.Tests.dll", null, true, false, 0, TimeSpan.FromSeconds(600));

        Assert.Equal("test", DotnetRunner.Arguments(request, "C:/slot")[0]);
    }

    [Fact]
    public void BuildArguments_SkipTheImplicitRestore_UnlessTheRetryAsksForIt()
    {
        Assert.Equal(
            ["build", "A.slnx", "-nodeReuse:false", "-v", "q", "--nologo", "--no-restore"],
            DotnetRunner.BuildArguments("A.slnx", default, restore: false));

        Assert.Equal(
            ["build", "A.slnx", "-nodeReuse:false", "-v", "q", "--nologo"],
            DotnetRunner.BuildArguments("A.slnx", default, restore: true));
    }

    [Fact]
    public void FailedForMissingAssets_OnlyWhenAFailedBuildSaysThePackagesAreNotThere()
    {
        Assert.True(DotnetRunner.FailedForMissingAssets(new ProcessRun(1, "error NETSDK1004: Assets file not found", 10)));
        Assert.True(DotnetRunner.FailedForMissingAssets(new ProcessRun(1, "error NETSDK1064: Package X was not found", 10)));
        Assert.True(DotnetRunner.FailedForMissingAssets(new ProcessRun(1, "error NU1102: Unable to find package", 10)));
        Assert.True(DotnetRunner.FailedForMissingAssets(new ProcessRun(1, "obj/project.assets.json not found", 10)));
        Assert.False(DotnetRunner.FailedForMissingAssets(new ProcessRun(1, "error CS0103: The name 'x' does not exist", 10)));
        Assert.False(DotnetRunner.FailedForMissingAssets(new ProcessRun(0, "error NETSDK1004: Assets file not found", 10)));
        Assert.False(DotnetRunner.FailedForMissingAssets(new ProcessRun(1, "error NETSDK1004: Assets file not found", 10, TimedOut: true)));
        Assert.False(DotnetRunner.FailedForMissingAssets(new ProcessRun(1, "error NETSDK1004: Assets file not found", 10, Stopped: true)));
    }

    [Fact]
    public void RestoreIsStale_BeforeThisServerHasRestored_IsTrueEvenWithTheAssetsInPlace()
    {
        var root = Directory.CreateTempSubdirectory("terse-restore-").FullName;

        try
        {
            var workspace = Restorable(root);

            Assert.True(DotnetRunner.RestoreIsStale(workspace));
            Assert.True(DotnetRunner.RestoreIsStale(workspace with { ProjectPaths = default }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RestoreIsStale_WithoutAnAssetsFile_IsTrue()
    {
        var root = Directory.CreateTempSubdirectory("terse-restore-").FullName;

        try
        {
            var workspace = Restorable(root);
            var assets = Path.Combine(Path.GetDirectoryName(workspace.ProjectPaths[0])!, "obj", "project.assets.json");

            Assert.True(File.Exists(assets));

            File.Delete(assets);

            Assert.True(DotnetRunner.RestoreIsStale(workspace));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RestoreInputStamp_MovesWhenAnythingRestoreReadsChanges_IncludingBetweenTheProjectAndTheRoot()
    {
        var root = Directory.CreateTempSubdirectory("terse-restore-").FullName;

        try
        {
            var workspace = Restorable(root);
            var project = workspace.ProjectPaths[0];
            var packages = Path.Combine(root, "src", "Directory.Packages.props");
            var unchanged = DotnetRunner.RestoreInputStamp(workspace);

            Assert.Equal(unchanged, DotnetRunner.RestoreInputStamp(workspace));

            File.SetLastWriteTimeUtc(project, File.GetLastWriteTimeUtc(project).AddMinutes(1));
            var afterProject = DotnetRunner.RestoreInputStamp(workspace);

            Assert.NotEqual(unchanged, afterProject);

            File.WriteAllText(packages, "<Project />");

            Assert.NotEqual(afterProject, DotnetRunner.RestoreInputStamp(workspace));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static WorkspaceTarget Restorable(string root)
    {
        var project = Path.Combine(root, "src", "Sample", "Sample.csproj");
        var assets = Path.Combine(root, "src", "Sample", "obj", "project.assets.json");

        Directory.CreateDirectory(Path.GetDirectoryName(assets)!);
        File.WriteAllText(project, "<Project />");
        File.WriteAllText(assets, "{}");

        return new WorkspaceTarget(Path.Combine(root, "A.slnx"), root, [project]);
    }

    [Fact]
    public void Pathological_ForAnAssemblyWhoseMeanTestCostsSeconds_NamesItAndItsRate()
    {
        var report = TestRunReport.Empty with
        {
            Passed = 494,
            Total = 494,
            DurationMs = 8_587_053,
            Projects =
            [
                new TestProjectSummary("Fast.Tests", 300, 0, 0, 300, 12_043),
                new TestProjectSummary("ArchitectureTests", 494, 0, 0, 494, 8_587_053),
            ],
        };

        var text = DotnetRunner.Pathological(report, "Whole.slnx");

        Assert.Contains("slowAssembly=ArchitectureTests", text, StringComparison.Ordinal);
        Assert.Contains("17382ms/test", text, StringComparison.Ordinal);
        Assert.Contains("pass slowest=10", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Pathological_ForASuiteWhoseMeanTestIsOrdinary_SaysNothing()
    {
        var report = TestRunReport.Empty with
        {
            Passed = 168,
            Total = 168,
            DurationMs = 110_328,
            Projects = [new TestProjectSummary("TerseSharp.E2ETests", 168, 0, 0, 168, 110_328)],
        };

        Assert.Equal(string.Empty, DotnetRunner.Pathological(report, "TerseSharp.slnx"));
    }

    [Fact]
    public void Pathological_ForASingleProjectRunThatReportsNoPerProjectSummary_UsesTheTargetName()
    {
        var report = TestRunReport.Empty with { Passed = 2, Total = 2, DurationMs = 40_000 };

        var text = DotnetRunner.Pathological(report, Path.Combine("tests", "Slow.Tests", "Slow.Tests.csproj"));

        Assert.Contains("slowAssembly=Slow.Tests", text, StringComparison.Ordinal);
        Assert.Contains("20000ms/test", text, StringComparison.Ordinal);
    }
}
