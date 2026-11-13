using System.Text;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class FlatContainerTests
{
    private static readonly byte[] Feed = Encoding.UTF8.GetBytes("""{"versions":["1.0.0","1.2.0","1.10.0","2.0.0-preview.2","2.0.0-preview.10"]}""");

    [Fact]
    public void Newer_PicksTheHighestStableVersionAboveTheCurrentOne() =>
        Assert.Equal(new FeedAnswer(true, "1.10.0"), FeedIndex.Newer(Feed, "1.2.0"));

    [Fact]
    public void Newer_ForAPrereleaseCurrentVersion_OrdersPrereleaseLabelsNumerically() =>
        Assert.Equal(new FeedAnswer(true, "2.0.0-preview.10"), FeedIndex.Newer(Feed, "2.0.0-preview.2"));

    [Fact]
    public void Newer_WhenNothingIsNewer_AnswersParsedWithNoVersion() =>
        Assert.Equal(new FeedAnswer(true, null), FeedIndex.Newer(Feed, "1.10.0"));

    [Theory]
    [InlineData("""{"error":"not found"}""")]
    [InlineData("""{"versions":"1.0.0"}""")]
    [InlineData("<html>404</html>")]
    [InlineData("""{"versions":["1.0.0",""")]
    public void Newer_OnABodyWithoutAVersionList_IsUnparsedRatherThanUpToDate(string body) =>
        Assert.Equal(FeedAnswer.Unparsed, FeedIndex.Newer(Encoding.UTF8.GetBytes(body), "1.0.0"));

    [Theory]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1.0.0.1", "1.0", 1)]
    [InlineData("1.0.0-beta", "1.0.0", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-1", 1)]
    [InlineData("1.0.0-BETA", "1.0.0-beta", 0)]
    [InlineData("1.0.0+build.7", "1.0.0", 0)]
    [InlineData("10.0.0", "9.9.9", 1)]
    public void Compare_OrdersVersionsTheWayNuGetDoes(string left, string right, int expected) =>
        Assert.Equal(expected, PackageVersionOrder.Compare(left, right));

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("1.2.3.4-rc.1", true)]
    [InlineData("$(AlphaVersion)", false)]
    [InlineData("[1.0,2.0)", false)]
    [InlineData("1.*", false)]
    [InlineData("1.2.3.4.5", false)]
    [InlineData("1.0.0-", false)]
    public void IsConcrete_AcceptsOnlyALiteralVersion(string version, bool expected) =>
        Assert.Equal(expected, PackageVersionOrder.IsConcrete(version));

    [Fact]
    public void Verdict_NamesTheCurrentAndTheNewestVersion_TaggedHeuristic() =>
        Assert.Equal("HEURISTIC  Alpha  1.2.0 -> 1.10.0", FlatContainer.Verdict(new PackagePin("Alpha", "1.2.0"), Feed));

    [Fact]
    public void Verdict_ForAVersionThatIsNotALiteralOrAFeedThatDidNotAnswer_SaysSoInsteadOfCallingItCurrent()
    {
        Assert.Equal("UNRESOLVED  Alpha  $(AlphaVersion)  its version is not a literal, so it was not looked up", FlatContainer.Verdict(new PackagePin("Alpha", "$(AlphaVersion)"), null));
        Assert.Equal("UNRESOLVED  Beta  -  its version is not a literal, so it was not looked up", FlatContainer.Verdict(new PackagePin("Beta", null), null));
        Assert.Equal("UNRESOLVED  Gamma  1.0.0  nuget.org returned no index for it", FlatContainer.Verdict(new PackagePin("Gamma", "1.0.0"), null));
    }

    [Fact]
    public async Task AuditAsync_ListsOnlyWhatIsOutdatedOrUnresolved_AndNeverFetchesANonLiteralVersion()
    {
        List<string> fetched = [];
        PackagePin[] pins = [new("Alpha", "1.2.0"), new("Current", "1.10.0"), new("Floating", "1.*")];

        var audit = await FlatContainer.AuditAsync(
            pins,
            (id, _) =>
            {
                fetched.Add(id);
                return Task.FromResult<byte[]?>(Feed);
            },
            TestContext.Current.CancellationToken);

        string[] expectedFetches = ["Alpha", "Current"];
        string[] expectedLines = ["HEURISTIC  Alpha  1.2.0 -> 1.10.0", "UNRESOLVED  Floating  1.*  its version is not a literal, so it was not looked up"];
        Assert.Equal(expectedFetches, fetched);
        Assert.Equal(3, audit.Examined);
        Assert.Equal(1, audit.Outdated);
        Assert.Equal(expectedLines, audit.Lines);
    }

    [Fact]
    public void Index_LowercasesThePackageId() =>
        Assert.Equal("https://api.nuget.org/v3-flatcontainer/microsoft.build/index.json", FlatContainer.Index("Microsoft.Build").AbsoluteUri);
}
