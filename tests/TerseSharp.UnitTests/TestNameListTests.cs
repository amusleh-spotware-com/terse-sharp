using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class TestNameListTests
{
    [Fact]
    public void Parse_ASolutionWithSeveralTestProjects_NamesEveryTest()
    {
        var names = TestNameList.Parse(SolutionListing, null);

        Assert.Contains(names, name => name.StartsWith("TerseSharp.UnitTests.", StringComparison.Ordinal));
        Assert.Contains(names, name => name.StartsWith("TerseSharp.E2ETests.", StringComparison.Ordinal));
        Assert.Equal(ExpectedCount, names.Length);
    }

    [Fact]
    public void Parse_ASolutionListing_KeepsNoBuildOutputLine()
    {
        var names = TestNameList.Parse(SolutionListing, null);

        Assert.DoesNotContain(names, name => name.Contains("->", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.Contains(".dll", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_WithContains_KeepsOnlyMatchingNames()
    {
        var names = TestNameList.Parse(SolutionListing, "LastTestRunTests");

        Assert.NotEmpty(names);
        Assert.All(names, name => Assert.Contains("LastTestRunTests", name, StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_AParameterizedName_IsKept()
    {
        var output = "The following Tests are available:\n    Ns.Tests.Theory(volume: 1)\n";

        Assert.Equal(["Ns.Tests.Theory(volume: 1)"], TestNameList.Parse(output, null));
    }

    [Fact]
    public void Parse_OutputWithoutAHeader_StillFindsTheIndentedNames()
    {
        var output = "    Ns.Tests.One\n    Ns.Tests.Two\n";

        Assert.Equal(["Ns.Tests.One", "Ns.Tests.Two"], TestNameList.Parse(output, null));
    }

    private static string SolutionListing { get; } =
        File.ReadAllText(Path.Combine(Fixtures.RepositoryRoot, "fixtures", "list-tests-solution.txt"));

    private static int ExpectedCount { get; } = SolutionListing
        .Split('\n')
        .Count(line => line.StartsWith("    ", StringComparison.Ordinal) && !line.Contains("->", StringComparison.Ordinal));

    [Fact]
    public void Parse_OfTheTestingPlatformDiscoveryOutput_ReadsTheTwoSpaceIndentedNames()
    {
        var names = TestNameList.Parse(PlatformDiscovery, null);

        Assert.Equal(
            [
                "Mtp.Trading.Tests.DeliberateMtpOutcomesTests.FailsAssertion",
            "Mtp.Trading.Tests.DeliberateMtpOutcomesTests.SkippedByDesign",
            "Mtp.Trading.Tests.LedgerTests.Balance_SubtractsTheDebitsFromTheCredits",
            "Mtp.Trading.Tests.LedgerTests.Balance_WithNoDebits_IsTheCredits(credits: 1)",
            "Mtp.Trading.Tests.LedgerTests.Balance_WithNoDebits_IsTheCredits(credits: 2)",
        ],
            names);
    }

    [Fact]
    public void Parse_OfTheTestingPlatformDiscoveryOutput_NeverKeepsTheSummaryCounters()
    {
        var names = TestNameList.Parse(PlatformDiscovery, null);

        Assert.DoesNotContain(names, name => name.StartsWith("duration", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.Contains("Test discovery summary", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_OfTheTestingPlatformDiscoveryOutput_DropsAnIndentedPreambleTheCountExcludes()
    {
        var noisy = "Telemetry\r\n  The platform collects usage data.\r\n  Read more at https://aka.ms/testing.\r\n\r\n" + PlatformDiscovery;

        Assert.Equal(5, TestNameList.Parse(noisy, null).Length);
    }

    private const string PlatformDiscovery =
        "xUnit.net v3 Microsoft.Testing.Platform v1 Runner v3.2.2+728c1dce01 (64-bit .NET 10.0.9)\r\n" +
        "\r\n" +
        "\r\n" +
        "  Mtp.Trading.Tests.LedgerTests.Balance_SubtractsTheDebitsFromTheCredits\r\n" +
        "  Mtp.Trading.Tests.LedgerTests.Balance_WithNoDebits_IsTheCredits(credits: 1)\r\n" +
        "  Mtp.Trading.Tests.LedgerTests.Balance_WithNoDebits_IsTheCredits(credits: 2)\r\n" +
        "  Mtp.Trading.Tests.DeliberateMtpOutcomesTests.FailsAssertion\r\n" +
        "  Mtp.Trading.Tests.DeliberateMtpOutcomesTests.SkippedByDesign\r\n" +
        "\r\n" +
        "Test discovery summary: found 5 test(s) - C:\\repo\\Mtp.Trading.Tests.dll (net10.0|x64)\r\n" +
        "  duration: 140ms\r\n";
}
