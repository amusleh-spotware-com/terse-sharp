using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class BuildScopeTests
{
    [Fact]
    public void Applied_WithNoScope_LeavesTheArgumentsUntouched()
    {
        var arguments = new BuildScope(null, null).Applied(["build", "A.slnx"]);

        Assert.Equal(["build", "A.slnx"], arguments);
    }

    [Fact]
    public void Applied_WithAConfiguration_PassesItAsMinusC()
    {
        var arguments = new BuildScope("Release", null).Applied(["build", "A.slnx"]);

        Assert.Equal(["build", "A.slnx", "-c", "Release"], arguments);
    }

    [Fact]
    public void Applied_WithATargetFramework_PassesItAsMinusF()
    {
        var arguments = new BuildScope(null, "net10.0").Applied(["test", "A.slnx"]);

        Assert.Equal(["test", "A.slnx", "-f", "net10.0"], arguments);
    }

    [Fact]
    public void Applied_WithBoth_PassesConfigurationBeforeTargetFramework()
    {
        var arguments = new BuildScope("Release", "net10.0").Applied(["build", "A.slnx"]);

        Assert.Equal(["build", "A.slnx", "-c", "Release", "-f", "net10.0"], arguments);
    }

    [Fact]
    public void Applied_WithEmptyStrings_TreatsThemAsAbsent()
    {
        var scope = new BuildScope(string.Empty, string.Empty);

        Assert.True(scope.IsDefault);
        Assert.Equal(["build"], scope.Applied(["build"]));
    }

    [Fact]
    public void Applied_WithProperties_PassesEachAsMinusP()
    {
        var arguments = new BuildScope(null, null, ["NativeAppHostEnabled=false", "ContinuousIntegrationBuild=true"])
            .Applied(["build", "A.slnx"]);

        Assert.Equal(
            ["build", "A.slnx", "-p:NativeAppHostEnabled=false", "-p:ContinuousIntegrationBuild=true"],
            arguments);
    }

    [Fact]
    public void Applied_WithEverything_PassesPropertiesLast()
    {
        var arguments = new BuildScope("Release", "net10.0", ["NativeAppHostEnabled=false"]).Applied(["build", "A.slnx"]);

        Assert.Equal(
            ["build", "A.slnx", "-c", "Release", "-f", "net10.0", "-p:NativeAppHostEnabled=false"],
            arguments);
    }

    [Fact]
    public void IsDefault_WithPropertiesOnly_IsFalse() =>
        Assert.False(new BuildScope(null, null, ["NativeAppHostEnabled=false"]).IsDefault);

    [Fact]
    public void IsDefault_WithAnEmptyPropertyList_IsTrue() =>
        Assert.True(new BuildScope(null, null, []).IsDefault);
}
