using Microsoft.CodeAnalysis;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class DiagnosticIdsTests
{
    [Theory]
    [InlineData("CA1822", "CA*", true)]
    [InlineData("CA1822", "ca*", true)]
    [InlineData("IDE0022", "IDE*", true)]
    [InlineData("xUnit1004", "XUNIT*", true)]
    [InlineData("CA1822", "IDE*", false)]
    [InlineData("CA1822", "CA1822", true)]
    [InlineData("CA1822", "ca1822", true)]
    [InlineData("CA18220", "CA1822", false)]
    [InlineData("CA1822", "CA18", false)]
    public void Matches_TreatsATrailingStarAsAPrefixAndEverythingElseAsAnExactId(string id, string entry, bool expected) =>
        Assert.Equal(expected, DiagnosticIds.Matches(id, entry));

    [Fact]
    public void Unsupported_WithAPrefixNoDeclaredIdStartsWith_NamesOnlyThatPrefix()
    {
        string[] ids = ["CA*", "CS*", "CS0*", "TERSE*", "TERSE001", "ZZ*", "CA9999"];
        string[] expected = ["ZZ*", "CA9999"];

        var unsupported = ProjectDiagnostics.Unsupported([], ids, [], ["CA1822"]);

        Assert.Equal(expected, unsupported);
    }

    [Fact]
    public void Wants_WithAPrefixId_KeepsEveryDiagnosticOfThatFamilyAndNoOther()
    {
        var request = new FixRequest(FixMode.All, ["CA*"], DiagnosticSeverity.Hidden, Verify: false);

        Assert.True(request.Wants(Raised("CA1822")));
        Assert.False(request.Wants(Raised("IDE0005")));
    }

    private static Diagnostic Raised(string id) => Diagnostic.Create(
        new DiagnosticDescriptor(id, "title", "message", "category", DiagnosticSeverity.Warning, isEnabledByDefault: true),
        Location.None);
}
