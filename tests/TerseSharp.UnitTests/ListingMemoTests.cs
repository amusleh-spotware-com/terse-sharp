using System.Diagnostics;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ListingMemoTests
{
    [Fact]
    public void Replay_WithTheSameStamp_AppendsTheUnchangedMarkerToThePreviousListing()
    {
        var memo = new ListingMemo();

        memo.Remember("key", "stamp", "2 files\na.md  +1 -0  M", 0);

        var replay = memo.Replay("key", "stamp", Stopwatch.Frequency);

        Assert.NotNull(replay);
        Assert.StartsWith("2 files\na.md  +1 -0  M", replay, StringComparison.Ordinal);
        Assert.Contains("UNCHANGED - no watcher event and no git state change since this exact call 1s ago", replay, StringComparison.Ordinal);
    }

    [Fact]
    public void Replay_WithADifferentStamp_AnswersNothing()
    {
        var memo = new ListingMemo();

        memo.Remember("key", "stamp-one", "2 files", 0);

        Assert.Null(memo.Replay("key", "stamp-two", 1));
        Assert.Null(memo.Replay("other-key", "stamp-one", 1));
    }
}
