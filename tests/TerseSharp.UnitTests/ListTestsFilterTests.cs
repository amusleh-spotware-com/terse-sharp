using TerseSharp.Server.Tools;

namespace TerseSharp.UnitTests;

public sealed class ListTestsFilterTests
{
    [Fact]
    public void FilterAsContains_ReadsAFullyQualifiedNameContainsFilterAsContains() =>
        Assert.Equal("PluralPathsTests", BuildTools.FilterAsContains("FullyQualifiedName~PluralPathsTests", null).Value);

    [Fact]
    public void FilterAsContains_RefusesAnExpressionItCannotEvaluate_NamingContains()
    {
        var refused = BuildTools.FilterAsContains("Name=Foo|Category=Slow", null);

        Assert.False(refused.IsOk);
        Assert.Contains("contains=", refused.Error!.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public void FilterAsContains_RefusesFilterBesideContains() =>
        Assert.False(BuildTools.FilterAsContains("FullyQualifiedName~X", "Y").IsOk);

    [Fact]
    public void FilterAsContains_WithNoFilter_KeepsContains() =>
        Assert.Equal("Y", BuildTools.FilterAsContains(null, "Y").Value);
}
