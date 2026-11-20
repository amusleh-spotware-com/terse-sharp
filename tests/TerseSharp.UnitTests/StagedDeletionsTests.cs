using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class StagedDeletionsTests
{
    [Fact]
    public void Intersect_APathDeletedOnDiskAndStillStaged_IsTheOnlyOneNamed()
    {
        var stale = StagedDeletions.Intersect("src/Old.cs\nsrc/Gone.cs\n", "src/Old.cs\nsrc/New.cs\n");

        Assert.Equal(["src/Old.cs"], stale);
    }

    [Fact]
    public void Intersect_NothingStaged_NamesNothing() => Assert.Empty(StagedDeletions.Intersect("src/Old.cs\n", string.Empty));

    [Fact]
    public void Warning_NamesEveryStalePathAndTheCommandsThatResolveIt()
    {
        var warning = StagedDeletions.Warning(["src/Old.cs", "tests/OldTests.cs"]);

        Assert.StartsWith("WARNING 2 file(s) still staged at a path this change removed from disk: src/Old.cs, tests/OldTests.cs", warning, StringComparison.Ordinal);
        Assert.Contains("git rm --cached", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Appeared_AStagedDeletionThatPredatesTheMove_IsNotNamed() => Assert.Equal(["src/Moved.cs"], StagedDeletions.Appeared(["src/Unrelated.cs"], ["src/Unrelated.cs", "src/Moved.cs"]));
}
