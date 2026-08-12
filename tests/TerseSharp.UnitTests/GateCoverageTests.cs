using TerseSharp.Core;

namespace TerseSharp.UnitTests;

[Collection(nameof(FixtureSolutionCollection))]
public sealed class GateCoverageTests
{
    [Fact]
    public void Once_AnswersTheNoticeOnceAndNothingAfterThat()
    {
        GateCoverage.Forget();

        var first = GateCoverage.Once();
        var second = GateCoverage.Once();

        Assert.Equal(GateCoverage.Unchecked, first);
        Assert.Null(second);

        GateCoverage.Forget();

        Assert.Equal(GateCoverage.Unchecked, GateCoverage.Once());
    }

    [Fact]
    public void Unchecked_NamesTheErrorClassesTheSemanticGateCannotSee()
    {
        Assert.Contains("emit-time", GateCoverage.Unchecked, StringComparison.Ordinal);
        Assert.Contains("source-generator", GateCoverage.Unchecked, StringComparison.Ordinal);
        Assert.Contains("run build once before you push", GateCoverage.Unchecked, StringComparison.Ordinal);
    }
}
