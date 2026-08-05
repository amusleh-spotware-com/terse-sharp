using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class LoadFailureSummaryTests
{
    private const string Advisory =
        "Msbuild failed when processing the file 'C:\\Projects\\cTraderDev\\Common\\Common.Domain\\Common.Domain.csproj' with message: Package 'SharpZipLib' 0.86.0 has a known moderate severity vulnerability";

    private const string SecondAdvisory =
        "Msbuild failed when processing the file 'C:\\Projects\\cTraderDev\\Common\\Common.Domain\\Common.Domain.csproj' with message: Package 'SharpZipLib' 0.86.0 has a known high severity vulnerability";

    private const string OtherProject =
        "Msbuild failed when processing the file 'C:\\Projects\\cTraderDev\\Core\\Core.Mapper\\Core.Mapper.csproj' with message: Package 'SharpZipLib' 0.86.0 has a known high severity vulnerability";

    [Fact]
    public void Group_WithNoFailures_ReturnsNothing() =>
        Assert.Empty(LoadFailureSummary.Group([]));

    [Fact]
    public void Group_WithTwoMessagesForOneProject_CountsThemOnOneLine()
    {
        var groups = LoadFailureSummary.Group([Advisory, SecondAdvisory]);

        Assert.Equal([new LoadFailureGroup("Common.Domain.csproj", 2)], groups);
    }

    [Fact]
    public void Group_WithSeveralProjects_KeepsFirstSeenOrder()
    {
        var groups = LoadFailureSummary.Group([Advisory, OtherProject, SecondAdvisory]);

        Assert.Equal(
            [new LoadFailureGroup("Common.Domain.csproj", 2), new LoadFailureGroup("Core.Mapper.csproj", 1)],
            groups);
    }

    [Fact]
    public void Group_WithAMessageNamingNoProject_KeepsTheMessageAsItsOwnGroup()
    {
        var groups = LoadFailureSummary.Group(["the solution file could not be read"]);

        Assert.Equal([new LoadFailureGroup("the solution file could not be read", 1)], groups);
    }

    [Fact]
    public void Group_WithAnUnattributedMessageLongerThanTheCap_TruncatesIt()
    {
        var groups = LoadFailureSummary.Group([new string('x', 400)]);

        Assert.Equal(123, groups[0].Project.Length);
        Assert.EndsWith("...", groups[0].Project, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectOf_WithAQuotedProjectPath_ReturnsTheFileName() =>
        Assert.Equal("Common.Domain.csproj", LoadFailureSummary.ProjectOf(Advisory).ToString());

    [Fact]
    public void ProjectOf_WhenAnEarlierQuotedRunIsNotAProject_KeepsLooking() =>
        Assert.Equal(
            "Core.Mapper.csproj",
            LoadFailureSummary.ProjectOf("Package 'SharpZipLib' broke 'C:\\repo\\Core.Mapper.csproj'").ToString());

    [Fact]
    public void ProjectOf_WithAnUnterminatedQuote_ReturnsNothing() =>
        Assert.True(LoadFailureSummary.ProjectOf("failed on 'C:\\repo\\Core.Mapper.csproj").IsEmpty);

    [Fact]
    public void ProjectOf_WithNoQuotes_ReturnsNothing() =>
        Assert.True(LoadFailureSummary.ProjectOf("the solution file could not be read").IsEmpty);
}
