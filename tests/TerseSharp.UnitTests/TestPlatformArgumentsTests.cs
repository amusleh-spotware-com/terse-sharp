using TerseSharp.Core;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class TestPlatformArgumentsTests
{
    [Fact]
    public void Of_ForAProject_AddressesItWithProjectRatherThanAPositionalPath()
    {
        var arguments = TestPlatformArguments.Of(Request("tests/Unit/Unit.csproj"), "results");

        Assert.Equal("test", arguments[0]);
        Assert.Equal("--project", arguments[1]);
        Assert.Equal("tests/Unit/Unit.csproj", arguments[2]);
        Assert.DoesNotContain("--logger", arguments);
        Assert.DoesNotContain("trx", arguments);
    }

    [Theory]
    [InlineData("Repo.slnx", "--solution")]
    [InlineData("Repo.sln", "--solution")]
    [InlineData("Repo.slnf", "--solution")]
    [InlineData("Repo.csproj", "--project")]
    public void Selector_NamesTheSwitchTheRunnerAccepts(string target, string expected) =>
        Assert.Equal(expected, TestPlatformArguments.Selector(target));

    [Fact]
    public void Of_ForAnXunitProject_AsksForTheXunitTrxReportAfterTheSeparator()
    {
        var arguments = TestPlatformArguments.Of(Request("Unit.csproj", TestReporter.XunitTrx), "results");
        var separator = Array.IndexOf(arguments, "--");

        Assert.True(separator > 0, "the forwarded block must be separated by --");
        Assert.Equal("--report-xunit-trx", arguments[separator + 1]);
        Assert.Equal("--report-xunit-trx-filename", arguments[separator + 2]);
        Assert.Equal("results.trx", arguments[separator + 3]);
    }

    [Fact]
    public void Of_ForANonXunitProject_AsksForThePlatformTrxReport()
    {
        var arguments = TestPlatformArguments.Of(Request("Unit.csproj", TestReporter.TestingPlatformTrx), "results");

        Assert.Contains("--report-trx", arguments);
        Assert.Contains("--report-trx-filename", arguments);
        Assert.DoesNotContain("--report-xunit-trx", arguments);
    }

    [Fact]
    public void Of_NeverPassesAVsTestOnlyArgumentTheRunnerWouldReject()
    {
        var arguments = TestPlatformArguments.Of(Request("Unit.csproj", TestReporter.XunitTrx), "results");

        Assert.DoesNotContain("--blame-hang-timeout", arguments);
        Assert.DoesNotContain("--blame-hang-dump-type", arguments);
        Assert.DoesNotContain("-nodeReuse:false", arguments);
        Assert.DoesNotContain("--nologo", arguments);
    }

    [Fact]
    public void Of_PassesTheHangWindowInTheUnitTheRunnerParses()
    {
        var arguments = TestPlatformArguments.Of(
            Request("Unit.csproj", TestReporter.XunitTrx) with { Timeout = TimeSpan.FromSeconds(300) },
            "results");
        var timeout = Array.IndexOf(arguments, "--timeout");

        Assert.True(timeout > 0, "a bounded run must carry a runner timeout");
        Assert.EndsWith("s", arguments[timeout + 1], StringComparison.Ordinal);
        Assert.DoesNotContain("ms", arguments[timeout + 1], StringComparison.Ordinal);
    }

    [Fact]
    public void Of_TellsTheRunnerNotToFailTheSessionForZeroTests()
    {
        var arguments = TestPlatformArguments.Of(Request("Unit.csproj", TestReporter.XunitTrx), "results");
        var ignored = Array.IndexOf(arguments, "--ignore-exit-code");

        Assert.True(ignored > 0, "zero tests must stay a terse warning rather than a runner failure");
        Assert.Equal("8", arguments[ignored + 1]);
    }

    [Fact]
    public void Filtered_ForAnExactXunitSelection_BecomesAFullyQualifiedMethodFilter() =>
        Assert.Equal(
            ["--filter-method", "Trading.OrderTests.Submits"],
            TestPlatformArguments.Filtered(TestReporter.XunitTrx, "FullyQualifiedName=Trading.OrderTests.Submits"));

    [Fact]
    public void Filtered_ForSeveralExactSelections_OrsThemUnderOneSwitch() =>
        Assert.Equal(
            ["--filter-method", "Trading.A.One", "Trading.B.Two"],
            TestPlatformArguments.Filtered(TestReporter.XunitTrx, "FullyQualifiedName=Trading.A.One|FullyQualifiedName=Trading.B.Two"));

    [Fact]
    public void Filtered_ForAContainsSelection_WildcardsBothEndsTheWayTheTildeMeans() =>
        Assert.Equal(
            ["--filter-method", "*Trading.OrderTests*"],
            TestPlatformArguments.Filtered(TestReporter.XunitTrx, "FullyQualifiedName~Trading.OrderTests"));

    [Fact]
    public void Filtered_UnescapesTheVsTestEscapesTerseAdded() =>
        Assert.Equal(
            ["--filter-method", "Trading.OrderTests.Submits(volume: 1)"],
            TestPlatformArguments.Filtered(TestReporter.XunitTrx, @"FullyQualifiedName=Trading.OrderTests.Submits\(volume: 1\)"));

    [Theory]
    [InlineData("Category=Smoke")]
    [InlineData("FullyQualifiedName~Ledger&Category=Fast")]
    [InlineData("FullyQualifiedName~Ledger|Category=Fast")]
    [InlineData("TestCategory!=Slow")]
    public void Untranslatable_ForAnExpressionXunitCannotSelectOnEveryVersion_IsRefusedRatherThanMistranslated(string filter) =>
        Assert.True(TestPlatformArguments.Untranslatable(TestReporter.XunitTrx, filter));

    [Theory]
    [InlineData("FullyQualifiedName=Trading.OrderTests.Submits")]
    [InlineData("FullyQualifiedName~Trading.OrderTests")]
    [InlineData("FullyQualifiedName=Trading.A.One|FullyQualifiedName=Trading.B.Two")]
    public void Untranslatable_ForTheSelectionTerseItselfBuilds_IsAccepted(string filter) =>
        Assert.False(TestPlatformArguments.Untranslatable(TestReporter.XunitTrx, filter));

    [Fact]
    public void Untranslatable_ForANonXunitRunner_NeverRefuses() =>
        Assert.False(TestPlatformArguments.Untranslatable(TestReporter.TestingPlatformTrx, "Category=Smoke"));

    [Fact]
    public void Filtered_ForANonXunitRunner_KeepsTheVsTestExpressionTheFrameworkUnderstands() =>
        Assert.Equal(
            ["--filter", "FullyQualifiedName~Trading"],
            TestPlatformArguments.Filtered(TestReporter.TestingPlatformTrx, "FullyQualifiedName~Trading"));

    private static TestRunRequest Request(string target, TestReporter reporter = TestReporter.XunitTrx) =>
        new(target, null, false, false, 0, TimeSpan.FromSeconds(300), Reporter: reporter);
}
