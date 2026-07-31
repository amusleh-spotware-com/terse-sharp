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
}
