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

    [Theory]
    [InlineData("FixtureSolution")]
    [InlineData("SelectionSolution")]
    [InlineData("RazorSolution")]
    public void Scan_OverEachFixture_AgreesWithTheAnswerTheLoadedIndexGives(string fixture)
    {
        var root = Path.Combine(Fixtures.RepositoryRoot, "fixtures", fixture);

        Assert.Equal(WorkspaceMarkup.Of(PathIndex.Build(root)), WorkspaceMarkup.Scan(root));
    }

    [Fact]
    public void Scan_OfADirectoryThatDoesNotExist_AdvertisesEveryFamilyRatherThanClaimingNone() =>
        Assert.Equal(WorkspaceMarkup.Every, WorkspaceMarkup.Scan(Path.Combine(Fixtures.RepositoryRoot, "fixtures", "NoSuchSolution")));

    [Fact]
    public void Scan_SkipsMarkupInsideExcludedDirectoriesAndSessionState()
    {
        var root = Directory.CreateTempSubdirectory("terse-markup-scan-");

        try
        {
            Write(root, Path.Combine("bin", "Debug", "Theme.xaml"));
            Write(root, Path.Combine("obj", "Strings.resx"));
            Write(root, Path.Combine(".claude", "Strings.resx"));
            Write(root, Path.Combine("Pages", "Index.razor"));

            Assert.Equal(new WorkspaceMarkup(Xaml: false, Razor: true, Resx: false), WorkspaceMarkup.Scan(root.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Scan_DoesNotFollowASymlinkedDirectory()
    {
        var root = Directory.CreateTempSubdirectory("terse-markup-scan-");
        var elsewhere = Directory.CreateTempSubdirectory("terse-markup-target-");

        try
        {
            Write(elsewhere, "Theme.xaml");
            Write(root, "Order.cs");
            LinkDirectoryOrSkip(Path.Combine(root.FullName, "linked"), elsewhere.FullName);

            Assert.Equal(default, WorkspaceMarkup.Scan(root.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
            elsewhere.Delete(recursive: true);
        }
    }

    private static void Write(DirectoryInfo root, string relativePath)
    {
        var full = Path.Combine(root.FullName, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
    }

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

    private static void LinkDirectoryOrSkip(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("this account may not create a directory symbolic link: " + exception.Message);
        }
    }
}
