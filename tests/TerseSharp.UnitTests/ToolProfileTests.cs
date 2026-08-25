using TerseSharp.Core;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ToolProfileTests
{
    [Fact]
    public void Resolve_WithNothingRequestedAndNoEnvironment_DerivesTheSurfaceFromTheWorkspace() =>
        Assert.Equal(new ToolSurface(null, MarkupDerived: true), ToolProfile.Resolve(null, null));

    [Fact]
    public void Resolve_WithAnEmptyEnvironmentValue_DerivesTheSurfaceFromTheWorkspace() =>
        Assert.Equal(new ToolSurface(null, MarkupDerived: true), ToolProfile.Resolve(null, string.Empty));

    [Theory]
    [InlineData("all")]
    [InlineData("ALL")]
    public void Resolve_WithAllRequested_AdvertisesEveryToolWhateverTheWorkspaceHolds(string requested) =>
        Assert.Equal(new ToolSurface(null, MarkupDerived: false), ToolProfile.Resolve(requested, null));

    [Fact]
    public void Resolve_WithAllInTheEnvironment_AdvertisesEveryToolWhateverTheWorkspaceHolds() =>
        Assert.Equal(new ToolSurface(null, MarkupDerived: false), ToolProfile.Resolve(null, "all"));

    [Theory]
    [InlineData("core")]
    [InlineData("Core")]
    public void Resolve_WithCoreRequested_AdvertisesTheCoreProfile(string requested)
    {
        var surface = ToolProfile.Resolve(requested, null);

        Assert.Same(ToolProfile.CoreTools, surface.Advertised);
        Assert.False(surface.MarkupDerived);
    }

    [Fact]
    public void Resolve_WithAnUnknownProfile_FallsBackToTheWorkspaceDerivedSurface() =>
        Assert.Equal(new ToolSurface(null, MarkupDerived: true), ToolProfile.Resolve("nonsense", null));

    [Fact]
    public void Resolve_WithAnExplicitRequest_OutranksTheEnvironment() =>
        Assert.Equal(new ToolSurface(null, MarkupDerived: false), ToolProfile.Resolve("all", "core"));

    [Fact]
    public void Describe_ForTheCoreProfile_NamesItAndSaysTheRestStillAnswer()
    {
        var described = ToolProfile.Describe(new ToolSurface(ToolProfile.CoreTools, MarkupDerived: false), default);

        Assert.NotNull(described);
        Assert.Contains("tools=core", described, StringComparison.Ordinal);
        Assert.Contains("still answers when called by name", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_ForAWorkspaceHoldingEveryMarkupKind_SaysNothing() =>
        Assert.Null(ToolProfile.Describe(new ToolSurface(null, MarkupDerived: true), WorkspaceMarkup.Every));

    [Fact]
    public void Describe_ForAWorkspaceHoldingNoMarkup_NamesEveryHiddenFamily()
    {
        var described = ToolProfile.Describe(new ToolSurface(null, MarkupDerived: true), default);

        Assert.NotNull(described);
        Assert.Contains("xaml_*, razor_*, resx_*", described, StringComparison.Ordinal);
        Assert.Contains("still answers when called by name", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_ForAnExplicitAllSurface_SaysNothingEvenWithoutMarkup() =>
        Assert.Null(ToolProfile.Describe(new ToolSurface(null, MarkupDerived: false), default));

    [Fact]
    public void Advertises_WithNoSettingsFile_FollowsTheProfileAndTheMarkup()
    {
        var surface = new ToolSurface(null, MarkupDerived: true);

        Assert.True(ToolProfile.Advertises(surface, WorkspaceMarkup.Every, "xaml_outline"));
        Assert.False(ToolProfile.Advertises(surface, default, "xaml_outline"));
        Assert.True(ToolProfile.Advertises(surface, default, "get_file_outline"));
    }

    [Fact]
    public void Advertises_WithAToolDisabledInTheFile_HidesItEvenUnderTheAllProfile()
    {
        var surface = new ToolSurface(null, MarkupDerived: false, ToolSettings.Parse("""{"tools":{"names":{"find_usages":false}}}""", ToolSettings.FileName));

        Assert.False(ToolProfile.Advertises(surface, WorkspaceMarkup.Every, "find_usages"));
        Assert.True(ToolProfile.Advertises(surface, WorkspaceMarkup.Every, "find_files"));
    }

    [Fact]
    public void Advertises_WithAGroupEnabledInTheFile_ShowsItEvenWhenTheWorkspaceHoldsNoMarkup()
    {
        var surface = new ToolSurface(null, MarkupDerived: true, ToolSettings.Parse("""{"tools":{"groups":{"xaml":true}}}""", ToolSettings.FileName));

        Assert.True(ToolProfile.Advertises(surface, default, "xaml_outline"));
        Assert.False(ToolProfile.Advertises(surface, default, "razor_outline"));
    }

    [Fact]
    public void Advertises_WithAToolEnabledInTheFile_ShowsItEvenUnderTheCoreProfile()
    {
        var surface = new ToolSurface(ToolProfile.CoreTools, MarkupDerived: false, ToolSettings.Parse("""{"tools":{"names":{"impact_of":true}}}""", ToolSettings.FileName));

        Assert.True(ToolProfile.Advertises(surface, WorkspaceMarkup.Every, "impact_of"));
        Assert.False(ToolProfile.Advertises(surface, WorkspaceMarkup.Every, "explore_symbol"));
    }

    [Fact]
    public void Describe_ForASettingsFileThatHidesAGroup_NamesTheFileTheCountAndTheKey()
    {
        var described = ToolProfile.Describe(
            new ToolSurface(null, MarkupDerived: true, ToolSettings.Parse("""{"tools":{"groups":{"resx":false}}}""", ToolSettings.FileName)),
            WorkspaceMarkup.Every);

        Assert.NotNull(described);
        Assert.Contains("tools=" + ToolSettings.FileName, described, StringComparison.Ordinal);
        Assert.Contains("(resx)", described, StringComparison.Ordinal);
        Assert.Contains("still answers when called by name", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_ForASettingsFileWithAnIgnoredKey_NamesTheKey()
    {
        var described = ToolProfile.Describe(
            new ToolSurface(null, MarkupDerived: false, ToolSettings.Parse("""{"tools":{"groups":{"xamll":false}}}""", ToolSettings.FileName)),
            WorkspaceMarkup.Every);

        Assert.NotNull(described);
        Assert.Contains("ignored xamll", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_ForASettingsFileThatCouldNotBeRead_NamesThePathAndTheReason()
    {
        var path = "/repo/" + ToolSettings.FileName;
        var overrides = ToolSettings.Parse("{\"tools\":", path);
        var described = ToolProfile.Describe(new ToolSurface(null, MarkupDerived: true, overrides), WorkspaceMarkup.Every);

        Assert.NotNull(overrides.Failure);
        Assert.NotNull(described);
        Assert.Contains(path, described, StringComparison.Ordinal);
        Assert.Contains(overrides.Failure, described, StringComparison.Ordinal);
        Assert.Contains("it narrows nothing", described, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_ForASettingsFileBesideTheCoreProfile_KeepsBothNotes()
    {
        var described = ToolProfile.Describe(
            new ToolSurface(ToolProfile.CoreTools, MarkupDerived: false, ToolSettings.Parse("""{"tools":{"names":{"impact_of":true}}}""", ToolSettings.FileName)),
            WorkspaceMarkup.Every);

        Assert.NotNull(described);
        Assert.Contains("tools=" + ToolSettings.FileName, described, StringComparison.Ordinal);
        Assert.Contains("tools=core", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_ForASettingsFileOverAWorkspaceWithNoMarkup_KeepsBothNotes()
    {
        var described = ToolProfile.Describe(
            new ToolSurface(null, MarkupDerived: true, ToolSettings.Parse("""{"tools":{"names":{"search_regex":false}}}""", ToolSettings.FileName)),
            default);

        Assert.NotNull(described);
        Assert.Contains("(search_regex)", described, StringComparison.Ordinal);
        Assert.Contains("xaml_*, razor_*, resx_* hidden", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_ForASettingsFileBesideTheCoreProfile_DropsTheCountItCannotProveAndSaysItOnce()
    {
        var described = ToolProfile.Describe(
            new ToolSurface(ToolProfile.CoreTools, MarkupDerived: false, ToolSettings.Parse("""{"tools":{"groups":{"xaml":false}}}""", ToolSettings.FileName)),
            WorkspaceMarkup.Every);

        Assert.NotNull(described);
        Assert.Contains("tools=core;", described + ";", StringComparison.Ordinal);
        Assert.DoesNotContain("advertised", described, StringComparison.Ordinal);
        Assert.Equal(1, described.Split("still answers when called by name").Length - 1);
    }
}
