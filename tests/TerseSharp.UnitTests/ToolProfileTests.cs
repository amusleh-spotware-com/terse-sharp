using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ToolProfileTests
{
    [Fact]
    public void Resolve_WithNothingRequestedAndNoEnvironment_AdvertisesEveryTool() =>
        Assert.Null(ToolProfile.Resolve(null, null));

    [Fact]
    public void Resolve_WithAnEmptyEnvironmentValue_AdvertisesEveryTool() =>
        Assert.Null(ToolProfile.Resolve(null, string.Empty));

    [Theory]
    [InlineData("all")]
    [InlineData("ALL")]
    public void Resolve_WithAllRequested_AdvertisesEveryTool(string requested) =>
        Assert.Null(ToolProfile.Resolve(requested, null));

    [Fact]
    public void Resolve_WithAllInTheEnvironment_AdvertisesEveryTool() =>
        Assert.Null(ToolProfile.Resolve(null, "all"));

    [Theory]
    [InlineData("core")]
    [InlineData("Core")]
    public void Resolve_WithCoreRequested_AdvertisesTheCoreProfile(string requested) =>
        Assert.Same(ToolProfile.CoreTools, ToolProfile.Resolve(requested, null));

    [Fact]
    public void Resolve_WithAnUnknownProfile_AdvertisesEveryToolRatherThanNarrowingSilently() =>
        Assert.Null(ToolProfile.Resolve("nonsense", null));

    [Fact]
    public void Resolve_WithAnExplicitRequest_OutranksTheEnvironment() =>
        Assert.Null(ToolProfile.Resolve("all", "core"));
}
