using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class DiagnosticScopeTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "terse-scope-root"));

    private static readonly string Generated =
        Path.Combine(Root, "obj", "Debug", "net10.0", "Thing.GlobalUsings.g.cs");

    private static readonly string Handwritten = Path.Combine(Root, "src", "Thing.cs");

    [Fact]
    public void Includes_AnErrorInAGeneratedFile_IsKept() =>
        Assert.True(new DiagnosticScope(Root, null).Includes(At(Generated, DiagnosticSeverity.Error)));

    [Theory]
    [InlineData(DiagnosticSeverity.Warning)]
    [InlineData(DiagnosticSeverity.Info)]
    [InlineData(DiagnosticSeverity.Hidden)]
    public void Includes_ANonErrorInAGeneratedFile_IsDropped(DiagnosticSeverity severity) =>
        Assert.False(new DiagnosticScope(Root, null).Includes(At(Generated, severity)));

    [Fact]
    public void Includes_AWarningInHandwrittenSource_IsKept() =>
        Assert.True(new DiagnosticScope(Root, null).Includes(At(Handwritten, DiagnosticSeverity.Warning)));

    [Fact]
    public void Includes_AGeneratedFileNamedExplicitly_IsKept() =>
        Assert.True(new DiagnosticScope(Root, Generated).Includes(At(Generated, DiagnosticSeverity.Info)));

    [Fact]
    public void Includes_AnotherFileWhenOneIsNamed_IsDropped() =>
        Assert.False(new DiagnosticScope(Root, Handwritten).Includes(At(Path.Combine(Root, "src", "Other.cs"), DiagnosticSeverity.Warning)));

    private static Diagnostic At(string file, DiagnosticSeverity severity) => Diagnostic.Create(
        new DiagnosticDescriptor("TEST001", "title", "message", "category", severity, isEnabledByDefault: true),
        Location.Create(file, new TextSpan(0, 1), new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 1))));
}
