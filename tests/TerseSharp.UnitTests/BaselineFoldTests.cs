using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class BaselineFoldTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "terse-baseline");

    private static readonly string Changed = Path.Combine(Root, "src", "Changed.cs");

    private static readonly SyntaxTree Tree = CSharpSyntaxTree.ParseText(
        string.Join('\n', Enumerable.Repeat("class C { }", 20)),
        path: Changed);

    private static readonly TouchedLines Touched = TouchedLines.From(
        "diff --git a/src/Changed.cs b/src/Changed.cs\n--- a/src/Changed.cs\n+++ b/src/Changed.cs\n@@ -3,0 +3,1 @@\n+class C { }\n",
        [],
        Root);

    [Fact]
    public void Split_ForAWarningOnAnUnchangedLine_FoldsItAsPreExisting()
    {
        var fold = BaselineFold.Split([Located(DiagnosticSeverity.Warning, line: 10)], Touched);

        Assert.Empty(fold.Kept);
        Assert.Equal(1, fold.PreExisting);
    }

    [Fact]
    public void Split_ForAnErrorOnAnUnchangedLine_KeepsItAndCountsItAsUnchanged()
    {
        var fold = BaselineFold.Split([Located(DiagnosticSeverity.Error, line: 10)], Touched);

        Assert.Single(fold.Kept);
        Assert.Equal(1, fold.UnchangedErrors);
        Assert.Equal(0, fold.PreExisting);
    }

    [Fact]
    public void Split_ForAWarningOnAChangedLine_KeepsItAndCountsNothingElse()
    {
        var fold = BaselineFold.Split([Located(DiagnosticSeverity.Warning, line: 3)], Touched);

        Assert.Single(fold.Kept);
        Assert.Equal(0, fold.UnchangedErrors);
        Assert.Equal(0, fold.PreExisting);
    }

    [Fact]
    public void Split_ForADiagnosticWithNoSourcePosition_CountsItApartAndNamesItsId()
    {
        var fold = BaselineFold.Split([Fileless("CS1705"), Fileless("CS1705"), Fileless("CS1701")], Touched);

        Assert.Empty(fold.Kept);
        Assert.Equal(3, fold.Fileless);
        Assert.Equal("CS1705 x2, CS1701 x1", fold.FilelessIds);
    }

    private static Diagnostic Located(DiagnosticSeverity severity, int line)
    {
        var span = Tree.GetText().Lines[line - 1].Span;

        return Diagnostic.Create(Descriptor("CS0001", severity), Location.Create(Tree, span));
    }

    private static Diagnostic Fileless(string id) =>
        Diagnostic.Create(Descriptor(id, DiagnosticSeverity.Error), Location.None);

    private static DiagnosticDescriptor Descriptor(string id, DiagnosticSeverity severity) =>
        new(id, id, id, "Compiler", severity, isEnabledByDefault: true);
}
