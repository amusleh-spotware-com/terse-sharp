using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class DiagnosticFoldTests
{
#pragma warning disable RS1029, RS2008
    private static readonly DiagnosticDescriptor Repeated = new(
        "TERSEP1",
        "Unnecessary expression value",
        "Expression value is never used",
        "Style",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor Other = new(
        "TERSEP2",
        "Mark members as static",
        "Member does not access instance data",
        "Performance",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);
#pragma warning restore RS1029, RS2008

    [Fact]
    public void Lines_FoldsRecordsSharingAnIdAndMessageOntoOneLineCarryingEveryPosition()
    {
        var lines = DiagnosticFold.Lines(Root, [At(Repeated, 0), At(Repeated, 12), At(Repeated, 24)], Head);

        Assert.Single(lines);
        Assert.Equal(3, lines[0].Split(", ").Length);
        Assert.Contains("Expression value is never used", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Lines_KeepsDistinctIdsApart()
    {
        var lines = DiagnosticFold.Lines(Root, [At(Repeated, 0), At(Other, 12)], Head);

        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, line => line.StartsWith("TERSEP2", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("TERSEP1", StringComparison.Ordinal));
    }

    [Fact]
    public void Lines_ForOneRecord_IsTheUnfoldedShape()
    {
        var lines = DiagnosticFold.Lines(Root, [At(Repeated, 0)], Head);

        Assert.Single(lines);
        Assert.DoesNotContain(", ", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Lines_ForTheSamePositionTwice_CountsItRatherThanRepeatingIt()
    {
        var lines = DiagnosticFold.Lines(Root, [At(Repeated, 0), At(Repeated, 0)], Head);

        Assert.Single(lines);
        Assert.Contains("x2", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Repeated_ForOneOccurrence_AddsNoCount() =>
        Assert.Equal("Foo.cs:1", DiagnosticFold.Repeated("Foo.cs:1", 1));

    private const string Root = "C:/repo";

    private static readonly SyntaxTree Tree = CSharpSyntaxTree.ParseText(
        "class Probe { void One() { } void Two() { } void Three() { } }",
        path: "C:/repo/Probe.cs");

    private static Diagnostic At(DiagnosticDescriptor descriptor, int offset) =>
        Diagnostic.Create(descriptor, Location.Create(Tree, new Microsoft.CodeAnalysis.Text.TextSpan(offset, 5)));

    private static string Head(Diagnostic diagnostic) =>
        diagnostic.Id + " " + diagnostic.Severity.ToString().ToLowerInvariant();

    [Fact]
    public void Lines_ForMoreThanTwentyPositions_CapsThemAndNamesHowManyItDropped()
    {
        var many = Enumerable.Range(0, 30).Select(index => At(Repeated, index)).ToArray();
        var lines = DiagnosticFold.Lines(Root, many, Head);

        Assert.Single(lines);
        Assert.Contains("+10 more", lines[0], StringComparison.Ordinal);
        Assert.Equal(21, lines[0].Split(", ").Length);
    }

    [Fact]
    public void Findings_KeyIsOneRecordPerOccurrence()
    {
        var findings = DiagnosticFold.Findings(Root, [At(Repeated, 0), At(Repeated, 0), At(Other, 12)], Head);

        Assert.Equal(3, findings.Length);
        Assert.Equal(findings[0].Key, findings[1].Key);
        Assert.NotEqual(findings[0].Key, findings[2].Key);
        Assert.Contains(": Expression value is never used", findings[0].Key, StringComparison.Ordinal);
    }

    [Fact]
    public void PerOccurrence_KeepsEveryCopyOfAByteIdenticalKeyDistinct()
    {
        var keys = DiagnosticFold.PerOccurrence(["a", "a", "a", "b"]);

        Assert.Equal(4, keys.Length);
        Assert.Equal(4, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("a", keys);
        Assert.Contains("a  [2]", keys);
        Assert.Contains("a  [3]", keys);
        Assert.Contains("b", keys);
    }

    [Fact]
    public void PerOccurrence_ForAKeyThatAlreadyLooksNumbered_StillKeepsEveryCopyDistinct()
    {
        var keys = DiagnosticFold.PerOccurrence(["a  [2 of 3]", "a", "a", "a"]);

        Assert.Equal(4, keys.Length);
        Assert.Equal(4, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void PerOccurrence_IsOrderIndependent()
    {
        Assert.Equal(
            DiagnosticFold.PerOccurrence(["b", "a", "a"]),
            DiagnosticFold.PerOccurrence(["a", "b", "a"]));
    }
}
