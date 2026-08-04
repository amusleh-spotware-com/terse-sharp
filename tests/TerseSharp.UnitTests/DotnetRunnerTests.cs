using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class DotnetRunnerTests
{
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
        var text = DotnetRunner.RenderBuild("A.slnx", Succeeded(WarningLine, SecondWarningLine), verbose: false);

        Assert.Equal("build ok  errors=0 warnings=2  elapsedMs=120  (verbose=true for the full report)", text);
    }

    [Fact]
    public void RenderBuild_WhenTheBuildSucceedsWithWarningsAndVerboseIsAsked_ListsThem()
    {
        var text = DotnetRunner.RenderBuild("A.slnx", Succeeded(WarningLine, SecondWarningLine), verbose: true);

        Assert.Contains("2 diagnostics (truncated=false, total=2)", text, StringComparison.Ordinal);
        Assert.Contains(WarningLine, text, StringComparison.Ordinal);
        Assert.Contains(SecondWarningLine, text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBuild_WhenTheBuildFails_ListsTheErrorsAndCountsTheHiddenWarnings()
    {
        var text = DotnetRunner.RenderBuild("A.slnx", Failed(WarningLine, ErrorLine, SecondWarningLine), verbose: false);

        Assert.Contains(ErrorLine, text, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0169", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CA1822", text, StringComparison.Ordinal);
        Assert.Contains("warnings=2 hidden (verbose=true for the full report)", text, StringComparison.Ordinal);
        Assert.Contains("1 diagnostics (truncated=true, total=3)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBuild_WhenTheBuildFailsAndVerboseIsAsked_ListsTheWarningsToo()
    {
        var text = DotnetRunner.RenderBuild("A.slnx", Failed(WarningLine, ErrorLine, SecondWarningLine), verbose: true);

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

        var text = DotnetRunner.RenderBuild("A.slnx", Failed(lockLine, WarningLine), verbose: false);

        Assert.Contains("WARNING a locked output file blocked the operation", text, StringComparison.Ordinal);
        Assert.Contains(lockLine, text, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0169", text, StringComparison.Ordinal);
        Assert.Contains("warnings=1 hidden (verbose=true for the full report)", text, StringComparison.Ordinal);
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
        Assert.Contains("warnings=2 hidden (verbose=true for the full report)", text, StringComparison.Ordinal);
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

        Assert.Equal("list_tests A.slnx\n0 tests (truncated=false, total=0)", text);
    }

    [Fact]
    public void RenderTestNames_WhenTheBuildFailed_ListsTheErrorsWithoutTheWarnings()
    {
        var text = DotnetRunner.RenderTestNames("A.slnx", Failed(WarningLine, ErrorLine), contains: null);

        Assert.Contains(ErrorLine, text, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0169", text, StringComparison.Ordinal);
        Assert.Contains("warnings=1 hidden (verbose=true for the full report)", text, StringComparison.Ordinal);
    }
}
