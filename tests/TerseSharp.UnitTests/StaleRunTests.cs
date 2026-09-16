using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class StaleRunTests
{
    [Fact]
    public void Note_WhenNothingWasWrittenWhileTheRunWasInFlight_IsNull() =>
        Assert.Null(StaleRun.Note(7, 7));

    [Fact]
    public void Note_WhenDocumentsChangedWhileTheRunWasInFlight_CountsThem()
    {
        var note = StaleRun.Note(7, 10);

        Assert.NotNull(note);
        Assert.StartsWith("STALE 3 document(s) changed after this run started", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Annotated_WhenNothingChanged_ReturnsTheVerdictUntouched() =>
        Assert.Equal("build ok  errors=0 warnings=0", StaleRun.Annotated("build ok  errors=0 warnings=0", 4, 4));

    [Fact]
    public void Annotated_WhenADocumentChanged_AppendsTheStaleLineBelowTheVerdict()
    {
        var annotated = StaleRun.Annotated("run_tests PASSED  passed=12", 4, 5);

        Assert.StartsWith("run_tests PASSED  passed=12\n", annotated, StringComparison.Ordinal);
        Assert.Contains("STALE 1 document(s) changed after this run started", annotated, StringComparison.Ordinal);
        Assert.EndsWith("re-run it", annotated, StringComparison.Ordinal);
    }

    [Fact]
    public void Annotated_WhenADocumentChanged_ProducesAVerdictTheRunMemoRefusesToRemember()
    {
        const string green = "build ok  errors=0 warnings=0";

        Assert.Null(UnchangedRun.MemoRefusal("build ok", green));
        Assert.NotNull(UnchangedRun.MemoRefusal("build ok", StaleRun.Annotated(green, 4, 5)));
    }
}
