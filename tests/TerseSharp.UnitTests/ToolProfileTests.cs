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
}
