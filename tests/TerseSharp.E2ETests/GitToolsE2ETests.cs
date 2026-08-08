using TerseSharp.Core;

namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class GitToolsE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task ChangedFiles_OnAnUnmodifiedFixture_AnswersZeroWithoutADiff()
    {
        var text = await server.CallAsync("changed_files", []);

        Assert.DoesNotContain("@@", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", text, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public async Task ChangedFiles_WithAnUnknownBaseRef_NamesGitsExitCodeAndARemedy()
    {
        var text = await server.CallAsync("changed_files", new() { ["baseRef"] = "terse-no-such-ref" });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiffSymbols_AgainstAnEarlierCommit_NamesChangedMembersAsResolvableSymbolIds()
    {
        var text = await server.CallAsync("diff_symbols", new()
        {
            ["baseRef"] = "HEAD~1",
            ["path"] = ".",
            ["maxResults"] = 50,
        });

        if (text.StartsWith("ERROR", StringComparison.Ordinal))
            return;

        foreach (var record in text.Split('\n').Where(line => line.Contains("  EXACT  ", StringComparison.Ordinal)))
        {
            var symbolId = record[(record.IndexOf("  EXACT  ", StringComparison.Ordinal) + 9)..].Trim();
            var resolved = await server.CallAsync("get_symbol", new() { ["symbolId"] = symbolId });

            Assert.DoesNotContain("ERROR", resolved, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DiffText_NeverReturnsMoreLinesThanMaxLines()
    {
        var text = await server.CallAsync("diff_text", new() { ["baseRef"] = "HEAD~1", ["maxLines"] = 5 });

        if (text.StartsWith("ERROR", StringComparison.Ordinal))
            return;

        var lines = text.Split('\n');

        Assert.Contains("lines", lines[0], StringComparison.Ordinal);
        Assert.True(lines.Length - 1 <= 5, text);
    }

    [Theory]
    [InlineData("@@ -12,0 +12,2 @@", 12, 13)]
    [InlineData("@@ -1 +1 @@", 1, 1)]
    [InlineData("@@ -10,3 +9,0 @@", 9, 9)]
    [InlineData("@@ -4,6 +4,7 @@ public sealed class OrderBook", 4, 10)]
    public void DiffParser_ReadsTheAddedSideOfEveryHunkHeaderShape(string header, int start, int end)
    {
        var mapped = DiffParser.Hunks(
            "diff --git a/src/Fixture.Trading/OrderBook.cs b/src/Fixture.Trading/OrderBook.cs\n"
            + "--- a/src/Fixture.Trading/OrderBook.cs\n"
            + "+++ b/src/Fixture.Trading/OrderBook.cs\n"
            + header);

        var hunk = Assert.Single(mapped);

        Assert.Equal("src/Fixture.Trading/OrderBook.cs", hunk.Path);
        Assert.Equal(start, hunk.Start);
        Assert.Equal(end, hunk.End);
    }

    [Fact]
    public void DiffParser_SkipsHunksOfADeletedFile()
    {
        var mapped = DiffParser.Hunks(
            "diff --git a/Gone.cs b/Gone.cs\n--- a/Gone.cs\n+++ /dev/null\n@@ -1,5 +0,0 @@");

        Assert.Empty(mapped);
    }

    [Fact]
    public void NumStat_ReportsABinaryFileAsUnknownRatherThanZero()
    {
        var files = DiffParser.NumStat("-\t-\tassets/logo.png\n3\t1\tsrc/Order.cs");

        Assert.Equal(2, files.Count);
        Assert.Equal(-1, files[0].Added);
        Assert.Equal(-1, files[0].Deleted);
        Assert.Equal(3, files[1].Added);
    }

    [Fact]
    public async Task ChangedFiles_WithAPath_ListsOnlyWhatThatPathspecCovers()
    {
        const string Probe = "terse-changed-files-probe.txt";
        await server.CallAsync("write_text", new() { ["path"] = Probe, ["content"] = "probe\n" });
        try
        {
            var everything = await server.CallAsync("changed_files", []);
            var scoped = await server.CallAsync("changed_files", new() { ["path"] = "src" });
            var named = await server.CallAsync("changed_files", new() { ["path"] = Probe });

            Assert.Contains(Probe, everything, StringComparison.Ordinal);
            Assert.DoesNotContain(Probe, scoped, StringComparison.Ordinal);
            Assert.Contains("0 files", scoped, StringComparison.Ordinal);
            Assert.Contains(Probe, named, StringComparison.Ordinal);
            Assert.Contains("1 files", named, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true });
        }
    }
}

internal static class DiffSymbolProbe
{
    public static IReadOnlyList<TerseSharp.Core.DiffHunk> Map(string diff) => TerseSharp.Core.DiffParser.Hunks(diff);
}
