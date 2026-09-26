using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class StampMoveTests
{
    private const string Root = "C:/repo@2026-09-26T00:00:00.0000000Z";

    [Fact]
    public void Named_WhenOnlyTheGenerationsMoved_NamesGenerationsWithBothCounts() =>
        Assert.Equal("generations 41->42", StampMove.Named($"pulse=1 loaded=1 {Root}=41", $"pulse=1 loaded=1 {Root}=42"));

    [Fact]
    public void Named_WhenThePulseMoved_NamesThePulseFirst() =>
        Assert.Equal("pulse 1->2", StampMove.Named($"pulse=1 loaded=1 {Root}=41", $"pulse=2 loaded=1 {Root}=42"));

    [Fact]
    public void Named_WhenAWorkspaceWasLoaded_NamesTheLoadedCount() =>
        Assert.Equal("loaded 1->2", StampMove.Named($"pulse=1 loaded=1 {Root}=41", $"pulse=1 loaded=2 {Root}=41 D:/other@2026-09-26T00:00:01.0000000Z=0"));

    [Fact]
    public void Named_WhenTheSameRootWasReloaded_NamesTheLoadedUtc() =>
        Assert.Equal(
            "LoadedUtc 2026-09-26T00:00:00.0000000Z->2026-09-26T00:05:00.0000000Z",
            StampMove.Named($"pulse=1 loaded=1 {Root}=41", "pulse=1 loaded=1 C:/repo@2026-09-26T00:05:00.0000000Z=0"));

    [Fact]
    public void Named_WhenOnlyTheSegmentCountDiffers_NamesBothCounts() =>
        Assert.Equal("segments 2->3", StampMove.Named("pulse=1 loaded=1", "pulse=1 loaded=1 extra=0"));

    [Fact]
    public void Appended_WithANote_PutsItOnItsOwnLine() =>
        Assert.Equal("run_tests PASSED  passed=1\nNOTE re-ran: x", StampMove.Appended("run_tests PASSED  passed=1", "NOTE re-ran: x"));

    [Fact]
    public void Appended_ToATextEndingInANewline_DoesNotAddABlankLine() =>
        Assert.Equal("1 failures\nNOTE re-ran: x", StampMove.Appended("1 failures\n", "NOTE re-ran: x"));

    [Fact]
    public void Appended_WithoutANote_ReturnsTheTextUnchanged() =>
        Assert.Equal("build ok", StampMove.Appended("build ok", null));
}
