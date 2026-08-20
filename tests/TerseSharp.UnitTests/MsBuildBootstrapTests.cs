using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class MsBuildBootstrapTests
{
    [Fact]
    public void Preferred_WhenTheLocatorPutsTheGlobalJsonSdkBeforeANewerBand_KeepsThePinnedSdk() =>
        Assert.Equal(0, MsBuildBootstrap.Preferred([new(10, 0, 300), new(10, 0, 400), new(10, 0, 303)], 10));

    [Fact]
    public void Preferred_WhenThePinnedSdkIsNotTheRuntimeMajor_TakesTheFirstCandidateThatIs() =>
        Assert.Equal(1, MsBuildBootstrap.Preferred([new(9, 0, 205), new(10, 0, 301), new(10, 0, 400)], 10));

    [Fact]
    public void Preferred_WhenNoCandidateMatchesTheRuntimeMajor_TakesTheLocatorsOwnFirstChoice() =>
        Assert.Equal(0, MsBuildBootstrap.Preferred([new(9, 0, 205), new(8, 0, 130)], 10));

    [Fact]
    public void Preferred_ForASingleCandidate_TakesIt() =>
        Assert.Equal(0, MsBuildBootstrap.Preferred([new(10, 0, 301)], 10));
}
