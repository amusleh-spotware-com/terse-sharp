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
        var fold = BaselineFold.Split([Fileless("CS1705"), Fileless("CS1705"), Fileless("CS1701", DiagnosticSeverity.Warning)], Touched);

        Assert.Empty(fold.Kept);
        Assert.Equal(3, fold.Fileless.Count);
        Assert.Equal("CS1705 x2, CS1701 x1", fold.Fileless.Ids);
        Assert.Equal(["CS1705", "CS1705"], fold.Fileless.Errors.Select(diagnostic => diagnostic.Id));
        Assert.Contains("2 of them error(s)", BaselineFold.FilelessNote(fold, "HEAD"), StringComparison.Ordinal);
    }

    private static Diagnostic Located(DiagnosticSeverity severity, int line)
    {
        var span = Tree.GetText().Lines[line - 1].Span;

        return Diagnostic.Create(Descriptor("CS0001", severity), Location.Create(Tree, span));
    }

    private static Diagnostic Fileless(string id, DiagnosticSeverity severity = DiagnosticSeverity.Error) =>
        Diagnostic.Create(Descriptor(id, severity), Location.None);

    private static DiagnosticDescriptor Descriptor(string id, DiagnosticSeverity severity) =>
        new(id, id, id, "Compiler", severity, isEnabledByDefault: true);

    [Fact]
    public void Split_ForAnAdditionalFileDiagnosticOnAChangedLine_KeepsIt()
    {
        var fold = BaselineFold.Split([External(DiagnosticSeverity.Warning, line: 3), External(DiagnosticSeverity.Warning, line: 10)], Touched);

        Assert.Single(fold.Kept);
        Assert.Equal(1, fold.PreExisting);
        Assert.Equal(0, fold.Fileless.Count);
    }

    private static Diagnostic External(DiagnosticSeverity severity, int line)
    {
        var text = Tree.GetText();
        var span = text.Lines[line - 1].Span;

        return Diagnostic.Create(Descriptor("RS0016", severity), Location.Create(Changed, span, text.Lines.GetLinePositionSpan(span)));
    }
}
