using TerseSharp.Core;
using Xunit;

namespace TerseSharp.UnitTests;

public sealed class ResponseBuilderTests
{
    [Fact]
    public void Summary_WhenTotalExceedsShown_MarksTruncated()
    {
        var text = new ResponseBuilder("find_usages", "M:A.B").Summary(2, 9, "usages").ToString();

        Assert.Equal("find_usages M:A.B\n2 usages (truncated=true, total=9)", text);
    }

    [Fact]
    public void Summary_WhenAllShown_MarksNotTruncated() =>
        Assert.Contains(
            "truncated=false",
            new ResponseBuilder("t", "a").Summary(3, 3, "items").ToString(),
            StringComparison.Ordinal);

    [Fact]
    public void Lines_AreOneRecordPerLine()
    {
        var text = new ResponseBuilder("t", "a").Summary(2, 2, "items").Line("first").Line("second").ToString();

        Assert.Equal(["t a", "2 items (truncated=false, total=2)", string.Empty, "first", "second"], text.Split('\n'));
    }

    [Fact]
    public void Header_WithoutArgument_HasNoTrailingSpace() =>
        Assert.StartsWith("list_workspaces\n", new ResponseBuilder("list_workspaces", string.Empty).Line("x").ToString(), StringComparison.Ordinal);
}
