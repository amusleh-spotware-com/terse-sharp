using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ReplacedContentTests
{
    [Theory]
    [InlineData("alpha\nbeta\ngamma\ndelta\n", "zeta\n", true)]
    [InlineData("alpha\nbeta\ngamma\ndelta\ntheta\n", "alpha\nzeta\n", true)]
    [InlineData("alpha\nbeta\ngamma\ndelta\n", "alpha\nzeta\n", false)]
    [InlineData("{\n}\n}\nalpha\nbeta\n", "{\n}\n}\nzeta\n", true)]
    [InlineData("  alpha  \nbeta\n", "alpha\r\nbeta\r\n", false)]
    [InlineData("\n\n{\n}\n", "zeta\n", false)]
    [InlineData("", "zeta\n", false)]
    [InlineData("alpha\nbeta\n", "", false)]
    public void Measure_RefusesOnlyAWriteThatKeepsUnderAQuarterOfTheContentLines(string before, string after, bool unrelated) =>
        Assert.Equal(unrelated, ReplacedContent.Measure(before, after).Unrelated);

    [Fact]
    public void Measure_CountsOnlyLinesCarryingALetterOrDigit() =>
        Assert.Equal(new ReplacedContent.Overlap(1, 2), ReplacedContent.Measure("namespace A;\n\n{\n    int Value;\n}\n", "namespace A;\n{\n}\n"));

    [Theory]
    [InlineData("x\n", "y\n", true, "overwrote existing  a.md  changedLines=1")]
    [InlineData("", "y\n", true, "a.md  changedLines=1")]
    [InlineData("x\n", "x\n", true, "a.md  changedLines=1")]
    [InlineData("x\n", "y\n", false, "a.md  changedLines=1")]
    public void Marked_PrefixesOnlyAQuietWriteThatChangedAFileWithContent(string before, string after, bool quiet, string expected) =>
        Assert.Equal(expected, ReplacedContent.Marked("a.md  changedLines=1", before, after, quiet));

    [Theory]
    [InlineData("WARNING policy  TERSE112 src/A.cs:3 comment\nsrc/A.cs  changedLines=1", "WARNING policy  TERSE112 src/A.cs:3 comment\noverwrote existing  src/A.cs  changedLines=1")]
    [InlineData("WARNING policy  TERSE101 changedLines= in a message\ngate=semantic - note\nsrc/A.cs  changedLines=2", "WARNING policy  TERSE101 changedLines= in a message\ngate=semantic - note\noverwrote existing  src/A.cs  changedLines=2")]
    [InlineData("no file line here", "overwrote existing  no file line here")]
    public void Marked_PrefixesTheFileLineAfterAnyPolicyWarningNotTheFirstLine(string text, string expected) =>
            Assert.Equal(expected, ReplacedContent.Marked(text, "x\n", "y\n", quiet: true));
}
