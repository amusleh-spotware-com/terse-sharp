using System.Globalization;
using TerseSharp.Core;

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

    [Fact]
    public void ChangedLines_ForTwoDistantEdits_CountsTheEditsNotTheSpanBetweenThem()
    {
        var (before, after) = DistantEdits();

        Assert.Equal(2, UnifiedDiff.ChangedLines(before, after));
    }

    [Fact]
    public void Between_ForTwoDistantEdits_EmitsOneHunkPerEditInsteadOfOneSpanningBoth()
    {
        var (before, after) = DistantEdits();

        var diff = UnifiedDiff.Between("a.cs", before, after);

        Assert.Equal(2, diff.Split('\n').Count(line => line.StartsWith("@@", StringComparison.Ordinal)));
        Assert.DoesNotContain("-line20", diff, StringComparison.Ordinal);
        Assert.Contains("-line2\n", diff, StringComparison.Ordinal);
        Assert.Contains("+THIRTYSEVEN", diff, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangedLines_ForAnInsertionInTheMiddle_CountsOnlyTheInsertedLines() =>
        Assert.Equal(2, UnifiedDiff.ChangedLines("a\nb\nc\nd\ne", "a\nb\nX\nY\nc\nd\ne"));

    private static (string Before, string After) DistantEdits()
    {
        var lines = Enumerable
            .Range(0, 40)
            .Select(index => "line" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var before = string.Join('\n', lines);

        return (
            before,
            before
                .Replace("line2\n", "TWO\n", StringComparison.Ordinal)
                .Replace("line37\n", "THIRTYSEVEN\n", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("", "a", 1)]
    [InlineData("a", "", 1)]
    [InlineData("only", "ONLY", 1)]
    public void ChangedLines_ForDegenerateInputs_CountsWhatChanged(string before, string after, int expected) =>
        Assert.Equal(expected, UnifiedDiff.ChangedLines(before, after));

    [Fact]
    public void Between_ForARegionTooLargeToAlign_FallsBackToOneBlockInsteadOfAllocatingTheTable()
    {
        var before = string.Join('\n', Enumerable.Range(0, 2100).Select(index => "a" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var after = string.Join('\n', Enumerable.Range(0, 2100).Select(index => "b" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        var diff = UnifiedDiff.Between("a.cs", before, after);

        Assert.Equal(1, diff.Split('\n').Count(line => line.StartsWith("@@", StringComparison.Ordinal)));
        Assert.Equal(2100, UnifiedDiff.ChangedLines(before, after));
    }

    [Fact]
    public void Between_NumbersEachHunkFromTheLineItStartsAt()
    {
        var diff = UnifiedDiff.Between("a.cs", "a\nb\nc\nd\ne", "a\nB\nc\nD\ne");

        Assert.Contains("@@ -2,1 +2,1 @@", diff, StringComparison.Ordinal);
        Assert.Contains("@@ -4,1 +4,1 @@", diff, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_AnswersTheSameTextAndCountAsTheTwoSeparateCalls()
    {
        var (before, after) = DistantEdits();

        var report = UnifiedDiff.Report("a.cs", before, after);

        Assert.Equal(UnifiedDiff.Between("a.cs", before, after), report.Text);
        Assert.Equal(UnifiedDiff.ChangedLines(before, after), report.ChangedLines);
    }

    [Fact]
    public void ChangedLines_ForTwoSmallEditsFarApartInALargeFile_CountsOnlyWhatChanged()
    {
        var before = Numbered(3000);
        var after = before
            .Replace("line 5\n", "line 5 changed\n", StringComparison.Ordinal)
            .Replace("line 2500\n", "line 2500 changed\n", StringComparison.Ordinal);

        var report = UnifiedDiff.Report("big.md", before, after);
        var hunks = report.Text.Split('\n').Count(line => line.StartsWith("@@", StringComparison.Ordinal));

        Assert.Equal(2, UnifiedDiff.ChangedLines(before, after));
        Assert.Equal(2, report.ChangedLines);
        Assert.Equal(2, hunks);
        Assert.DoesNotContain("-line 1000", report.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangedLines_ForAPureInsertionIntoALargeFile_CountsTheInsertedLinesOnly()
    {
        var before = Numbered(3000);
        var after = before.Replace("line 2900\n", "line 2900\ninserted a\ninserted b\n", StringComparison.Ordinal);

        Assert.Equal(2, UnifiedDiff.ChangedLines(before, after));
    }

    private static string Numbered(int lines) => string.Join(
        '\n',
        Enumerable.Range(0, lines).Select(index => "line " + index.ToString(CultureInfo.InvariantCulture)));

    [Fact]
    public void Report_WhenAnInsertionShiftsTheLinesBeforeTheAnchor_KeepsTheBeforeAndAfterOffsetsApart()
    {
        var before = Numbered(3000);
        var after = before
            .Replace("line 5\n", "line 5\ninserted a\ninserted b\n", StringComparison.Ordinal)
            .Replace("line 2500\n", "line 2500 changed\n", StringComparison.Ordinal);

        var report = UnifiedDiff.Report("big.md", before, after);
        var hunks = report.Text.Split('\n').Where(line => line.StartsWith("@@", StringComparison.Ordinal)).ToArray();

        Assert.Equal(3, report.ChangedLines);
        Assert.Equal(["@@ -7,0 +7,2 @@", "@@ -2501,1 +2503,1 @@"], hunks);
    }
}
