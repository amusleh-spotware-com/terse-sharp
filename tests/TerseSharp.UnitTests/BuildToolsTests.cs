using TerseSharp.Server;
using TerseSharp.Server.Tools;

namespace TerseSharp.UnitTests;

public sealed class BuildToolsTests
{
    private static readonly BuildScope PreviousRun =
        new("Release", "net10.0", ["NativeAppHostEnabled=false"]);

    [Fact]
    public void Remembered_WithNothingRequested_ReusesEveryPartOfThePreviousRun()
    {
        var scope = BuildTools.Remembered(PreviousRun, new BuildScope(null, null));

        Assert.Equal("Release", scope.Configuration);
        Assert.Equal("net10.0", scope.TargetFramework);
        Assert.Equal(["NativeAppHostEnabled=false"], scope.Properties);
    }

    [Fact]
    public void Remembered_WithAnEmptyPropertyList_StillReusesTheRememberedProperties()
    {
        var scope = BuildTools.Remembered(PreviousRun, new BuildScope(null, null, []));

        Assert.Equal(["NativeAppHostEnabled=false"], scope.Properties);
    }

    [Fact]
    public void Remembered_WithRequestedProperties_ReplacesTheRememberedOnesWholesale()
    {
        var scope = BuildTools.Remembered(PreviousRun, new BuildScope(null, null, ["ContinuousIntegrationBuild=true"]));

        Assert.Equal(["ContinuousIntegrationBuild=true"], scope.Properties);
        Assert.Equal("Release", scope.Configuration);
    }

    [Fact]
    public void Remembered_WithRequestedConfiguration_LeavesTheRememberedPropertiesAlone()
    {
        var scope = BuildTools.Remembered(PreviousRun, new BuildScope("Debug", null));

        Assert.Equal("Debug", scope.Configuration);
        Assert.Equal("net10.0", scope.TargetFramework);
        Assert.Equal(["NativeAppHostEnabled=false"], scope.Properties);
    }
}
