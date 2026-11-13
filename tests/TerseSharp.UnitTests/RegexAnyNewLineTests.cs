using System.Text.RegularExpressions;

namespace TerseSharp.UnitTests;

public sealed class RegexAnyNewLineTests
{
    [Fact]
    public void AnyNewLine_AnchorsDollarBeforeACarriageReturnOnTheBacktrackingEngineOnly()
    {
        var regex = new Regex("^### (Added|Fixed)$", RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.AnyNewLine, TimeSpan.FromSeconds(2));

        Assert.Equal(2, regex.Count("### Added\r\nbody\r\n### Fixed\r\n"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Regex("x$", RegexOptions.NonBacktracking | RegexOptions.AnyNewLine));
    }
}
