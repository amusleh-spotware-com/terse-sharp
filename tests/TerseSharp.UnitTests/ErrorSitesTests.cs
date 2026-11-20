using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ErrorSitesTests
{
    [Fact]
    public void Describe_ErrorsInSeveralFiles_RanksTheFilesByCountAndFoldsTheRest()
    {
        var errors = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["CS0103 src/A.cs: The name 'x' does not exist"] = 2,
            ["CS0246 src/A.cs: The type 'Y' could not be found"] = 8,
            ["CS1002 tests/B.cs: ; expected"] = 3,
            ["CS0029 src/C.cs: Cannot convert"] = 1,
            ["CS0029 src/D.cs: Cannot convert"] = 1,
        };

        Assert.Equal("errors in scope: src/A.cs 10, tests/B.cs 3, src/C.cs 1 +1 more file(s)", ErrorSites.Describe(errors, introduced: 0));
    }

    [Fact]
    public void Describe_WhenTheEditIntroducedErrors_SaysNothingBecauseTheRejectionNamesThem() => Assert.Null(ErrorSites.Describe(new Dictionary<string, int> { ["CS0103 src/A.cs: x"] = 1 }, introduced: 1));

    [Fact]
    public void Joined_CountersAndSites_PutsTheSitesOnTheNextLine() => Assert.Equal("errors=+0 (2 in scope)\nerrors in scope: src/A.cs 2", ErrorSites.Joined("errors=+0 (2 in scope)", "errors in scope: src/A.cs 2"));
}
