using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class WorkspaceLimitTests
{
    [Fact]
    public void Resolve_WithNeitherSource_KeepsTheDefault() =>
        Assert.Equal(WorkspaceLimit.Default, WorkspaceLimit.Resolve(null, null));

    [Fact]
    public void Resolve_WithTheOption_TakesIt() =>
        Assert.Equal(1, WorkspaceLimit.Resolve(1, null));

    [Fact]
    public void Resolve_WithOnlyTheEnvironmentVariable_TakesIt() =>
        Assert.Equal(2, WorkspaceLimit.Resolve(null, "2"));

    [Fact]
    public void Resolve_WithBoth_PrefersTheOption() =>
        Assert.Equal(3, WorkspaceLimit.Resolve(3, "7"));

    [Theory]
    [InlineData("many")]
    [InlineData("")]
    public void Resolve_WithAnUnparsableEnvironmentVariable_KeepsTheDefault(string environment) =>
        Assert.Equal(WorkspaceLimit.Default, WorkspaceLimit.Resolve(null, environment));

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public void Resolve_WithAParsableButOutOfRangeEnvironmentVariable_KeepsTheDefault(string environment) =>
        Assert.Equal(WorkspaceLimit.Default, WorkspaceLimit.Resolve(null, environment));

    [Fact]
    public void Resolve_WithASpacePaddedEnvironmentVariable_StillTakesIt() =>
        Assert.Equal(2, WorkspaceLimit.Resolve(null, " 2 "));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Resolve_WithAnUnusableOption_KeepsTheDefaultRatherThanTheEnvironmentVariable(int option) =>
        Assert.Equal(WorkspaceLimit.Default, WorkspaceLimit.Resolve(option, "5"));
}
