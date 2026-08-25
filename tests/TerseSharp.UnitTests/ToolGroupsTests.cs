using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ToolGroupsTests
{
    [Fact]
    public void All_HoldsEveryToolExactlyOnce()
    {
        var named = ToolGroups.All.SelectMany(group => group.Value).ToArray();

        Assert.NotEmpty(named);
        Assert.Equal(named.Length, named.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(named.Length, ToolGroups.Tools.Count);
    }

    [Fact]
    public void All_NamesTheMarkupFamiliesAfterTheToolPrefixTheyCarry()
    {
        Assert.Contains("xaml_outline", ToolGroups.All["xaml"]);
        Assert.Contains("razor_outline", ToolGroups.All["razor"]);
        Assert.Contains("resx_get", ToolGroups.All["resx"]);
        Assert.DoesNotContain("get_file_outline", ToolGroups.All["xaml"]);
    }

    [Fact]
    public void Of_ForAToolAndForANonTool_AnswersTheGroupOrNothing()
    {
        Assert.Equal("navigation", ToolGroups.Of("find_usages"));
        Assert.Equal("build", ToolGroups.Of("run_tests"));
        Assert.Null(ToolGroups.Of("find_usage"));
    }

    [Fact]
    public void Names_ListsEveryGroupOnce()
    {
        var names = ToolGroups.Names().Split(", ");

        Assert.Equal(ToolGroups.All.Count, names.Length);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("xaml", names);
        Assert.Contains("navigation", names);
    }

    [Fact]
    public void Named_ForAToolWrittenInAnotherCase_AnswersTheAdvertisedSpelling()
    {
        Assert.Equal("search_regex", ToolGroups.Named("Search_Regex"));
        Assert.Equal("search_regex", ToolGroups.Named("search_regex"));
        Assert.Null(ToolGroups.Named("search_regexp"));
    }
}
