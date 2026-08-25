using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ToolSettingsTests
{
    [Fact]
    public void Parse_WithNoToolsObject_HidesNothing()
    {
        var overrides = ToolSettings.Parse("""{"other":{"groups":{"xaml":false}}}""", ToolSettings.FileName);

        Assert.False(overrides.Configured);
        Assert.Null(overrides.Decision("xaml_outline"));
    }

    [Fact]
    public void Parse_WithAGroupDisabled_HidesEveryToolOfThatGroup()
    {
        var overrides = ToolSettings.Parse("""{"tools":{"groups":{"xaml":false}}}""", ToolSettings.FileName);

        Assert.All(ToolGroups.All["xaml"], tool => Assert.False(overrides.Decision(tool)));
        Assert.Equal(ToolGroups.All["xaml"].Length, overrides.Hidden);
        Assert.Null(overrides.Decision("get_file_outline"));
        Assert.Equal("xaml", Assert.Single(overrides.Off));
    }

    [Fact]
    public void Parse_WithANameEnabledInsideADisabledGroup_KeepsThatOneTool()
    {
        var overrides = ToolSettings.Parse("""{"tools":{"groups":{"xaml":false},"names":{"xaml_outline":true}}}""", ToolSettings.FileName);

        Assert.True(overrides.Decision("xaml_outline"));
        Assert.False(overrides.Decision("xaml_find"));
        Assert.Equal(ToolGroups.All["xaml"].Length - 1, overrides.Hidden);
    }

    [Fact]
    public void Parse_WithANameDisabled_HidesThatToolAndNothingElse()
    {
        var overrides = ToolSettings.Parse("""{"tools":{"names":{"search_regex":false}}}""", ToolSettings.FileName);

        Assert.False(overrides.Decision("search_regex"));
        Assert.Null(overrides.Decision("search_text"));
        Assert.Equal(1, overrides.Hidden);
    }

    [Fact]
    public void Parse_WithBuildAsAGroupAndAsAName_TellsTheFamilyFromTheSingleTool()
    {
        var family = ToolSettings.Parse("""{"tools":{"groups":{"build":false}}}""", ToolSettings.FileName);
        var single = ToolSettings.Parse("""{"tools":{"names":{"build":false}}}""", ToolSettings.FileName);

        Assert.False(family.Decision("run_tests"));
        Assert.False(family.Decision("build"));
        Assert.Null(single.Decision("run_tests"));
        Assert.False(single.Decision("build"));
    }

    [Fact]
    public void Parse_WithAnUnknownGroupOrName_NamesItRatherThanDroppingItSilently()
    {
        var overrides = ToolSettings.Parse("""{"tools":{"groups":{"xamll":false},"names":{"read_txt":true}}}""", ToolSettings.FileName);

        Assert.Empty(overrides.Tools);
        Assert.Contains("xamll", overrides.Ignored);
        Assert.Contains("read_txt", overrides.Ignored);
    }

    [Fact]
    public void Parse_WithAValueThatIsNotABoolean_NamesTheKeyRatherThanGuessing()
    {
        var overrides = ToolSettings.Parse("""{"tools":{"groups":{"xaml":"no"}}}""", ToolSettings.FileName);

        Assert.Contains("xaml", overrides.Ignored);
        Assert.Null(overrides.Decision("xaml_outline"));
    }

    [Fact]
    public void Parse_WithMalformedJson_ReportsTheFailureAndHidesNothing()
    {
        var overrides = ToolSettings.Parse("{\"tools\":", ToolSettings.FileName);

        Assert.NotNull(overrides.Failure);
        Assert.True(overrides.Configured);
        Assert.Equal(0, overrides.Hidden);
        Assert.Null(overrides.Decision("xaml_outline"));
    }

    [Fact]
    public void Notice_ForAnUnknownKey_NamesTheKeyAndEveryValidGroup()
    {
        var notice = ToolSettings.Notice(ToolSettings.Parse("""{"tools":{"groups":{"xamll":false}}}""", ToolSettings.FileName));

        Assert.NotNull(notice);
        Assert.Contains("xamll", notice, StringComparison.Ordinal);
        Assert.Contains(ToolGroups.Names(), notice, StringComparison.Ordinal);
    }

    [Fact]
    public void Notice_ForAFileThatOnlyHidesAGroup_SaysNothing() =>
        Assert.Null(ToolSettings.Notice(ToolSettings.Parse("""{"tools":{"groups":{"xaml":false}}}""", ToolSettings.FileName)));

    [Fact]
    public async Task LoadAsync_FromADirectoryBelowTheFile_FindsItByWalkingUp()
    {
        var root = Directory.CreateTempSubdirectory("terse-settings");

        try
        {
            var nested = root.CreateSubdirectory("src").CreateSubdirectory("App");
            var path = Path.Combine(root.FullName, ToolSettings.FileName);

            await File.WriteAllTextAsync(path, """{"tools":{"groups":{"razor":false}}}""", TestContext.Current.CancellationToken);

            var overrides = await ToolSettings.LoadAsync(nested.FullName, TestContext.Current.CancellationToken);

            Assert.Equal(path, overrides.Path);
            Assert.False(overrides.Decision("razor_outline"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_WithNoFileToFind_HidesNothing()
    {
        var root = Directory.CreateTempSubdirectory("terse-settings");

        try
        {
            var overrides = await ToolSettings.LoadAsync(root.FullName, TestContext.Current.CancellationToken);

            Assert.False(overrides.Configured);
            Assert.Null(overrides.Path);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Parse_WithToolsThatIsNotAnObject_NamesItRatherThanDroppingItSilently()
    {
        var overrides = ToolSettings.Parse("""{"tools":[{"groups":{"xaml":false}}]}""", ToolSettings.FileName);

        Assert.True(overrides.Configured);
        Assert.Equal("tools", Assert.Single(overrides.Ignored));
        Assert.Null(overrides.Decision("xaml_outline"));
        Assert.NotNull(ToolSettings.Notice(overrides));
    }

    [Fact]
    public void Parse_WithGroupsThatIsNotAnObject_NamesThatSectionAndKeepsTheOther()
    {
        var overrides = ToolSettings.Parse("""{"tools":{"groups":["xaml"],"names":{"search_regex":false}}}""", ToolSettings.FileName);

        Assert.Equal("groups", Assert.Single(overrides.Ignored));
        Assert.False(overrides.Decision("search_regex"));
        Assert.Null(overrides.Decision("xaml_outline"));
    }

    [Fact]
    public void Parse_WithANameWrittenInAnotherCase_ResolvesItToTheAdvertisedTool()
    {
        var overrides = ToolSettings.Parse("""{"tools":{"names":{"Search_Regex":false}}}""", ToolSettings.FileName);

        Assert.False(overrides.Decision("search_regex"));
        Assert.Empty(overrides.Ignored);
    }

    [Fact]
    public async Task LoadAsync_WithAFilePastTheSizeCeiling_FailsOpenAndSaysWhy()
    {
        var root = Directory.CreateTempSubdirectory("terse-settings");

        try
        {
            var path = Path.Combine(root.FullName, ToolSettings.FileName);

            await File.WriteAllTextAsync(path, new string(' ', 70 * 1024), TestContext.Current.CancellationToken);

            var overrides = await ToolSettings.LoadAsync(root.FullName, TestContext.Current.CancellationToken);

            Assert.NotNull(overrides.Failure);
            Assert.Contains("ceiling", overrides.Failure, StringComparison.Ordinal);
            Assert.Equal(0, overrides.Hidden);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Parse_WithAKeyDirectlyUnderTools_NamesItRatherThanDroppingItSilently()
    {
        var overrides = ToolSettings.Parse("""{"tools":{"xaml":false}}""", ToolSettings.FileName);

        Assert.Equal("xaml", Assert.Single(overrides.Ignored));
        Assert.Null(overrides.Decision("xaml_outline"));
        Assert.NotNull(ToolSettings.Notice(overrides));
    }

    [Fact]
    public void Parse_WithASectionExplicitlyNull_NamesThatSectionAndKeepsTheOther()
    {
        var overrides = ToolSettings.Parse("""{"tools":{"groups":null,"names":{"search_regex":false}}}""", ToolSettings.FileName);

        Assert.Equal("groups", Assert.Single(overrides.Ignored));
        Assert.False(overrides.Decision("search_regex"));
    }

    [Fact]
    public async Task LoadAsync_FromInsideARepository_DoesNotWalkAboveItsRoot()
    {
        var root = Directory.CreateTempSubdirectory("terse-settings");

        try
        {
            var repository = root.CreateSubdirectory("repo");
            var nested = repository.CreateSubdirectory("src");

            repository.CreateSubdirectory(".git");

            await File.WriteAllTextAsync(
                Path.Combine(root.FullName, ToolSettings.FileName),
                """{"tools":{"groups":{"razor":false}}}""",
                TestContext.Current.CancellationToken);

            var overrides = await ToolSettings.LoadAsync(nested.FullName, TestContext.Current.CancellationToken);

            Assert.False(overrides.Configured);
            Assert.Null(overrides.Path);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_FromInsideAWorktreeWhoseGitIsAFile_DoesNotWalkAboveItsRoot()
    {
        var root = Directory.CreateTempSubdirectory("terse-settings");

        try
        {
            var worktree = root.CreateSubdirectory("worktree");
            var nested = worktree.CreateSubdirectory("src");

            await File.WriteAllTextAsync(
                Path.Combine(worktree.FullName, ".git"),
                "gitdir: ../repo/.git/worktrees/worktree",
                TestContext.Current.CancellationToken);

            await File.WriteAllTextAsync(
                Path.Combine(root.FullName, ToolSettings.FileName),
                """{"tools":{"groups":{"razor":false}}}""",
                TestContext.Current.CancellationToken);

            var overrides = await ToolSettings.LoadAsync(nested.FullName, TestContext.Current.CancellationToken);

            Assert.False(overrides.Configured);
            Assert.Null(overrides.Path);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
