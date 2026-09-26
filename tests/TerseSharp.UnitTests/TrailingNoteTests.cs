using ModelContextProtocol.Protocol;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class TrailingNoteTests
{
    [Fact]
    public void Append_AfterAPayloadEndingMidLine_StartsTheNoteOnItsOwnLineWhenTheClientConcatenatesTheBlocks()
    {
        var result = Result("replace_symbol applied  src/A.cs  changedLines=41");

        TrailingNote.Append(result, "2 replace_symbol calls in a row - pass symbolIds=[...] with the next 2+ in ONE call");

        Assert.Equal(
            "replace_symbol applied  src/A.cs  changedLines=41\n2 replace_symbol calls in a row - pass symbolIds=[...] with the next 2+ in ONE call",
            Concatenated(result));
    }

    [Fact]
    public void Append_TwoNotesAfterOnePayload_PutsEachOnItsOwnLine()
    {
        var result = Result("rerun_failed PASSED  passed=3 warnings=0");

        TrailingNote.Append(result, "repeat #3 of this exact rerun_failed call 4s ago - previous verdict: rerun_failed PASSED; nothing was written in between");
        TrailingNote.Append(result, "2 rerun_failed calls in a row");

        Assert.Equal(
            "rerun_failed PASSED  passed=3 warnings=0\nrepeat #3 of this exact rerun_failed call 4s ago - previous verdict: rerun_failed PASSED; nothing was written in between\n2 rerun_failed calls in a row",
            Concatenated(result));
    }

    [Fact]
    public void Append_AfterAPayloadThatAlreadyEndsItsLine_AddsNoBlankLine()
    {
        var result = Result("payload\n");

        TrailingNote.Append(result, "note");

        Assert.Equal("payload\nnote", Concatenated(result));
    }

    [Fact]
    public void Append_ToAResultWithNoPayload_AddsTheNoteAsItIs()
    {
        var result = new CallToolResult { Content = [] };

        TrailingNote.Append(result, "note");

        Assert.Equal("note", Concatenated(result));
    }

    [Fact]
    public void NoServerSource_AppendsAContentBlockExceptThroughTrailingNote()
    {
        var appenders = Directory.EnumerateFiles(Path.Combine(Fixtures.RepositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path) is var text
                && (text.Contains("Content.Add(", StringComparison.Ordinal) || text.Contains("Content.Insert(", StringComparison.Ordinal)))
            .Select(path => Path.GetFileName(path.AsSpan()).ToString())
            .ToArray();

        Assert.Equal(["TrailingNote.cs"], appenders);
    }

    private static CallToolResult Result(string text) => new() { Content = [new TextContentBlock { Text = text }] };

    private static string Concatenated(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
