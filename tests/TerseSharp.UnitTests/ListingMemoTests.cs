using System.Diagnostics;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ListingMemoTests
{
    [Fact]
    public void Replay_WithTheSameStamp_AppendsTheUnchangedMarkerToThePreviousListing()
    {
        var memo = new ListingMemo();

        memo.Remember("key", "stamp", null, "2 files\na.md  +1 -0  M", 0);

        var replay = memo.Replay("key", "stamp", Stopwatch.Frequency);

        Assert.NotNull(replay);
        Assert.StartsWith("2 files\na.md  +1 -0  M", replay, StringComparison.Ordinal);
        Assert.Contains("UNCHANGED - no watcher event and no git state change since this exact call 1s ago", replay, StringComparison.Ordinal);
    }

    [Fact]
    public void Replay_WithADifferentStamp_AnswersNothing()
    {
        var memo = new ListingMemo();

        memo.Remember("key", "stamp-one", null, "2 files", 0);

        Assert.Null(memo.Replay("key", "stamp-two", 1));
        Assert.Null(memo.Replay("other-key", "stamp-one", 1));
    }

    [Fact]
    public void Remember_AfterAListingUnderADifferentStamp_NamesTheComponentThatMoved()
    {
        var memo = new ListingMemo();

        Assert.Null(memo.Remember("key", "generations=c1/p0 events=4 index=10:5", null, "1 files", 0));
        Assert.Equal("NOTE re-ran: stamp moved index 10:5->11:5", memo.Remember("key", "generations=c1/p0 events=4 index=11:5", null, "1 files", 1));
    }

    [Fact]
    public void Remember_WithNoStamp_NamesWhyTheStampWasUnavailableAndNeverReplays()
    {
        var memo = new ListingMemo();

        memo.Remember("key", "stamp", null, "1 files", 0);

        Assert.Equal("NOTE re-ran: no stamp watch=Active gaps=0 pending=2", memo.Remember("key", null, "watch=Active gaps=0 pending=2", "1 files", 1));
        Assert.Null(memo.Replay("key", "stamp", 2));
        Assert.Equal("NOTE re-ran: the previous call took no stamp", memo.Remember("key", "stamp", null, "1 files", 3));
    }

    [Fact]
    public void Remember_UnderTheSameStamp_AddsNoNote()
    {
        var memo = new ListingMemo();

        memo.Remember("key", "stamp", null, "1 files", 0);

        Assert.Null(memo.Remember("key", "stamp", null, "1 files", 1));
    }
}
