using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class AssetBannerTests
{
    [Fact]
    public void Notice_WhenEveryAssetIsInstalled_SaysNothing() =>
        Assert.Null(AssetBanner.Notice(new AssetState(true, true, true, true)));

    [Fact]
    public void Notice_WhenTheGuardIsAbsent_NamesTheInstallCommand()
    {
        var notice = AssetBanner.Notice(new AssetState(true, true, false, false));

        Assert.Equal(
            "WARNING guard=absent - nothing stops an agent answering with Read, Grep, cat or dotnet build; run: terse install --guard",
            notice);
    }

    [Fact]
    public void Notice_WhenTheSkillIsAbsent_NamesTheInstallCommand()
    {
        var notice = AssetBanner.Notice(new AssetState(false, false, true, true));

        Assert.Equal(
            "WARNING skill=absent - the agent has no tool guide for this server; run: terse install --skill",
            notice);
    }

    [Fact]
    public void Notice_WhenBothAreAbsent_CarriesBothLines()
    {
        var notice = AssetBanner.Notice(new AssetState(false, false, false, false))!;

        Assert.Equal(2, notice.Split('\n').Length);
        Assert.Contains("guard=absent", notice, StringComparison.Ordinal);
        Assert.Contains("skill=absent", notice, StringComparison.Ordinal);
    }

    [Fact]
    public void Notice_WhenAnAssetIsInstalledButStale_SaysNothingBecauseTheServerRewritesIt() =>
        Assert.Null(AssetBanner.Notice(new AssetState(true, false, true, false)));
}
