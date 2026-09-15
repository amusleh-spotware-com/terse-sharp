using System.Globalization;
using TerseSharp.Server.Tools;

namespace TerseSharp.UnitTests;

public sealed class GitToolsTests
{
    private const string Local = "v0.59.0 06ed47e 2026-09-14\nv0.58.0 bfd4768 2026-09-12";

    private const string Remote = "bfd4768111111111111111111111111111111111\trefs/tags/v0.58.0\n9c1d2e3444444444444444444444444444444444\trefs/tags/v0.60.0";

    [Fact]
    public void MergedTags_ForATagCutLocallyButNeverPushed_SaysRemoteNo()
    {
        var merged = GitTools.MergedTags(Local, Remote);

        Assert.Contains("v0.59.0 06ed47e 2026-09-14  local=yes remote=no", merged, StringComparison.Ordinal);
        Assert.Contains("v0.58.0 bfd4768 2026-09-12  local=yes remote=yes", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void MergedTags_ForATagOnlyTheRemoteHas_ListsItWithItsShortSha()
    {
        var merged = GitTools.MergedTags(Local, Remote);

        Assert.Contains("v0.60.0 9c1d2e3  local=no remote=yes", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void MergedTags_ForThePeeledRefLineTheRemoteAlsoEmits_CountsThatTagOnce()
    {
        var peeled = Remote + "\nbfd4768111111111111111111111111111111111\trefs/tags/v0.58.0^{}";

        var merged = GitTools.MergedTags(Local, peeled);

        Assert.Equal(3, merged.Split('\n').Length);
        Assert.DoesNotContain("^{}", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void MergedTags_WithNoRemoteTagsAtAll_StillAnswersEveryLocalTag()
    {
        var merged = GitTools.MergedTags(Local, string.Empty);

        Assert.Equal(2, merged.Split('\n').Length);
        Assert.DoesNotContain("remote=yes", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void MergedTags_ForATagOnlyTheRemoteHas_ListsItBeforeEveryLocalRowSoTheCapCannotDropIt()
    {
        var merged = GitToolsTests.Padded(60);

        Assert.StartsWith("v9.9.9 9c1d2e3  local=no remote=yes", merged, StringComparison.Ordinal);
    }

    private static string Padded(int locals)
    {
        var local = new System.Text.StringBuilder();

        for (var index = 0; index < locals; index++)
            local.Append(CultureInfo.InvariantCulture, $"v0.{index}.0 abc{index:0000} 2026-01-01\n");

        return GitTools.MergedTags(local.ToString(), "9c1d2e3444444444444444444444444444444444\trefs/tags/v9.9.9");
    }
}
