using System.Globalization;
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

    [Fact]
    public void Selection_WithSeveralTests_CombinesThemIntoOneFilterExpression()
    {
        var selection = BuildTools.Selection(null, ["OrderBookTests", "OrderServiceTests.Submits"], null);

        Assert.True(selection.IsOk);
        Assert.Equal("FullyQualifiedName~OrderBookTests|FullyQualifiedName~OrderServiceTests.Submits", selection.Value);
    }

    [Fact]
    public void Selection_WithTestAndTests_KeepsBothAndPutsTheSingleFirst()
    {
        var selection = BuildTools.Selection("First", ["Second"], null);

        Assert.Equal("FullyQualifiedName~First|FullyQualifiedName~Second", selection.Value);
    }

    [Fact]
    public void Selection_WithTestsAndAFilter_RefusesInsteadOfSilentlyDroppingOne()
    {
        var selection = BuildTools.Selection(null, ["First"], "FullyQualifiedName=Other");

        Assert.False(selection.IsOk);
        Assert.Contains("cannot be combined", selection.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Selection_WithABlankEntry_RefusesItByIndex()
    {
        var selection = BuildTools.Selection(null, ["First", ""], null);

        Assert.False(selection.IsOk);
        Assert.Contains("tests[1]", selection.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Selection_WithMoreEntriesThanTheCap_RefusesRatherThanTruncating()
    {
        var selection = BuildTools.Selection(null, [.. Enumerable.Range(0, 11).Select(index => index.ToString(CultureInfo.InvariantCulture))], null);

        Assert.False(selection.IsOk);
        Assert.Contains("11 entries", selection.Error!.Message, StringComparison.Ordinal);
    }
}
