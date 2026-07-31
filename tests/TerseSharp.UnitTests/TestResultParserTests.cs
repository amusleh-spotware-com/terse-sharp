using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class TestResultParserTests
{
    [Fact]
    public void Parse_AVstestReport_CountsEveryOutcome()
    {
        var report = Parse("xunit-vstest.trx");

        Assert.Equal(1, report.Passed);
        Assert.Equal(2, report.Failed);
        Assert.Equal(1, report.Skipped);
        Assert.Equal(4, report.Total);
    }

    [Fact]
    public void Parse_AnAssertionFailure_KeepsTheExpectedAndActualValues()
    {
        var failure = Failure("xunit-vstest.trx", "FailsAssertion");

        Assert.Contains("Assert.Equal() Failure", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Expected: 4", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Actual:   5", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_AThrownException_KeepsTheTypeAndMessage()
    {
        var failure = Failure("xunit-vstest.trx", "Throws");

        Assert.Equal("System.InvalidOperationException : probe boom", failure.Message);
    }

    [Fact]
    public void Parse_AFailure_ReportsOneWorkspaceRelativeFrame()
    {
        var failure = Failure("xunit-vstest.trx", "Throws");

        Assert.Equal("fixtures/FixtureSolution/tests/Fixture.Trading.Tests/DeliberateOutcomesTests.cs:24", failure.Frame);
    }

    [Fact]
    public void Parse_AFailure_SkipsFramesOutsideTheWorkspace()
    {
        var failure = Failure("xunit-vstest.trx", "Throws");

        Assert.DoesNotContain("System.Reflection", failure.Frame!, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_AReport_OrdersFailuresByName()
    {
        var report = Parse("xunit-vstest.trx");

        Assert.Equal(
            ["Fixture.Trading.Tests.DeliberateOutcomesTests.FailsAssertion", "Fixture.Trading.Tests.DeliberateOutcomesTests.Throws"],
            report.Failures.Select(failure => failure.Name));
    }

    [Fact]
    public void Parse_TwoFailuresSharingOneMessage_ReportsBoth()
    {
        var report = Parse("duplicate-messages.trx");

        Assert.Equal(2, report.Failed);
        Assert.Equal(2, report.Failures.Length);
        Assert.Equal(report.Failures[0].Message, report.Failures[1].Message);
    }

    [Fact]
    public void Parse_AnEmptyRun_ReportsNoTests()
    {
        var report = Parse("empty-run.trx");

        Assert.Equal(0, report.Total);
        Assert.Empty(report.Failures);
    }

    [Fact]
    public void Parse_ATruncatedReport_IsTreatedAsEmpty()
    {
        var report = Parse("crashed-run.trx");

        Assert.Equal(0, report.Total);
        Assert.Empty(report.Failures);
    }

    [Fact]
    public void Parse_AnMtpReport_ProducesTheSameCountersAsVstest()
    {
        var vstest = Parse("xunit-vstest.trx");
        var mtp = Parse("xunit-mtp.trx");

        Assert.Equal((vstest.Passed, vstest.Failed, vstest.Skipped, vstest.Total), (mtp.Passed, mtp.Failed, mtp.Skipped, mtp.Total));
    }

    [Fact]
    public void Parse_AnMtpReport_KeepsTheFrameAndMessage()
    {
        var failure = Failure("xunit-mtp.trx", "FailsAssertion");

        Assert.Contains("Expected: 4", failure.Message, StringComparison.Ordinal);
        Assert.Equal("fixtures/FixtureSolution/tests/Fixture.Trading.Tests/DeliberateOutcomesTests.cs:18", failure.Frame);
    }

    [Fact]
    public void Parse_SeveralReports_SumsTheirCounters()
    {
        var report = TestResultParser.Parse([Fixtures.Trx("xunit-vstest.trx"), Fixtures.Trx("xunit-mtp.trx")], Fixtures.TrxRoot);

        Assert.Equal(8, report.Total);
        Assert.Equal(4, report.Failed);
        Assert.Equal(4, report.Failures.Length);
    }

    [Fact]
    public void Slowest_RanksFailedAndPassedTestsTogetherByDuration()
    {
        var slowest = Parse("xunit-mtp.trx").Slowest(2).ToArray();

        Assert.Equal("Fixture.Trading.Tests.DeliberateOutcomesTests.Throws", slowest[0].Name);
        Assert.Equal(6, slowest[0].DurationMs);
        Assert.Equal("Fixture.Trading.Tests.DeliberateOutcomesTests.Passes", slowest[1].Name);
    }

    [Fact]
    public void Parse_AReport_SumsEveryResultDuration()
    {
        var report = Parse("xunit-mtp.trx");

        Assert.Equal(11, report.DurationMs);
    }

    [Fact]
    public void Parse_AFrameInASiblingDirectoryThatExtendsTheRoot_IsNotReported()
    {
        var failure = Parse("sibling-root.trx").Failures.Single();

        Assert.Null(failure.Frame);
    }

    [Fact]
    public void Parse_SeveralReports_OrdersEveryFailureByName()
    {
        var report = TestResultParser.Parse([Fixtures.Trx("xunit-mtp.trx"), Fixtures.Trx("duplicate-messages.trx")], Fixtures.TrxRoot);

        Assert.Equal(report.Failures.Select(failure => failure.Name).Order(StringComparer.Ordinal), report.Failures.Select(failure => failure.Name));
    }

    private static TestRunReport Parse(string name) => TestResultParser.Parse([Fixtures.Trx(name)], Fixtures.TrxRoot);

    private static TestFailure Failure(string name, string test) =>
        Parse(name).Failures.Single(failure => failure.Name.EndsWith(test, StringComparison.Ordinal));
}
