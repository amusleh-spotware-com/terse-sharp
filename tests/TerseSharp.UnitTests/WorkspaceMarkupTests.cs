using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class WorkspaceMarkupTests
{
    [Theory]
    [InlineData("xaml_outline", true, false, false)]
    [InlineData("razor_outline", false, true, false)]
    [InlineData("resx_get", false, false, true)]
    public void Serves_GatesOnlyTheFamilyThePrefixNames(string tool, bool xaml, bool razor, bool resx)
    {
        Assert.True(WorkspaceMarkup.Every.Serves(tool));
        Assert.False(default(WorkspaceMarkup).Serves(tool));
        Assert.True(new WorkspaceMarkup(xaml, razor, resx).Serves(tool));
    }

    [Theory]
    [InlineData("get_file_outline")]
    [InlineData("read_text")]
    [InlineData("build")]
    [InlineData("clean")]
    public void Serves_LeavesEveryToolOutsideTheThreeFamiliesAdvertised(string tool) =>
        Assert.True(default(WorkspaceMarkup).Serves(tool));

    [Fact]
    public void Union_TakesEveryFamilyEitherSideHolds() =>
        Assert.Equal(
            new WorkspaceMarkup(true, false, true),
            new WorkspaceMarkup(true, false, false).Union(new WorkspaceMarkup(false, false, true)));

    [Fact]
    public void Hidden_NamesOnlyTheFamiliesThatAreAbsent()
    {
        Assert.Equal("xaml_*, razor_*, resx_*", default(WorkspaceMarkup).Hidden());
        Assert.Equal("razor_*", new WorkspaceMarkup(true, false, true).Hidden());
        Assert.Equal(string.Empty, WorkspaceMarkup.Every.Hidden());
    }

    [Fact]
    public void Of_OverTheFixtureSolution_FindsEveryMarkupFamilyItHolds()
    {
        var markup = WorkspaceMarkup.Of(PathIndex.Build(Path.GetDirectoryName(Fixtures.SolutionPath)!));

        Assert.True(markup.Xaml);
        Assert.True(markup.Resx);
        Assert.True(markup.Razor);
        Assert.True(markup.Complete);
    }

    [Fact]
    public void Of_OverASolutionWithNoMarkupAtAll_FindsNothing()
    {
        var root = Path.Combine(Fixtures.RepositoryRoot, "fixtures", "SelectionSolution");

        Assert.Equal(default, WorkspaceMarkup.Of(PathIndex.Build(root)));
    }

    [Fact]
    public void Of_OverTheRazorSolution_FindsItsRazorFiles() =>
        Assert.True(WorkspaceMarkup.Of(PathIndex.Build(Path.GetDirectoryName(Fixtures.RazorSolutionPath)!)).Razor);

    [Fact]
    public async Task MarkupFamilies_AskedTwice_AnswersTheSecondCallFromTheIndexInsteadOfWalkingAgain()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        var workspace = registry.All()[0];

        Assert.Equal(workspace.Indexes.MarkupFamilies(), workspace.Indexes.MarkupFamilies());
        Assert.True(workspace.Indexes.MarkupFamilies().Complete);
        Assert.Contains("markup(hit=2 miss=1)", workspace.Indexes.Describe(), StringComparison.Ordinal);
    }
}
