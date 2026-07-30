using TerseSharp.Core;
using Xunit;

namespace TerseSharp.UnitTests;

public sealed class UnifiedDiffTests
{
    [Fact]
    public void Between_SingleChangedLine_ProducesOneAddedAndOneRemovedLine()
    {
        var diff = UnifiedDiff.Between("a.cs", "one\ntwo\nthree", "one\nTWO\nthree");

        Assert.Contains("-two", diff, StringComparison.Ordinal);
        Assert.Contains("+TWO", diff, StringComparison.Ordinal);
        Assert.DoesNotContain("-one", diff, StringComparison.Ordinal);
        Assert.DoesNotContain("-three", diff, StringComparison.Ordinal);
    }

    [Fact]
    public void Between_IdenticalText_ProducesNoAddedOrRemovedLines()
    {
        var diff = UnifiedDiff.Between("a.cs", "same", "same");

        var body = diff.Split('\n').SkipWhile(line => !line.StartsWith("@@", StringComparison.Ordinal)).Skip(1);

        Assert.Equal(0, UnifiedDiff.ChangedLines("same", "same"));
        Assert.DoesNotContain(body, line => line.Length > 0);
    }

    [Theory]
    [InlineData("a\nb", "a\nb", 0)]
    [InlineData("a\nb", "a\nB", 1)]
    [InlineData("a\nb\nc", "a\nX\nY\nc", 2)]
    public void ChangedLines_CountsTheChangedRegion(string before, string after, int expected) =>
        Assert.Equal(expected, UnifiedDiff.ChangedLines(before, after));

    [Fact]
    public void Between_NormalisesWindowsLineEndings()
    {
        var diff = UnifiedDiff.Between("a.cs", "one\r\ntwo", "one\r\nTWO");

        Assert.Contains("+TWO", diff, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", diff, StringComparison.Ordinal);
    }
}
