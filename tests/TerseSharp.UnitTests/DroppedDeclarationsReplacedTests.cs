using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class DroppedDeclarationsReplacedTests
{
    private const string Nested = "namespace N;\npublic sealed class Outer\n{\n    private readonly record struct Anchor(int First, int Count)\n    {\n        public Anchor With(int index) => this;\n\n        public bool Adjacent => Count > 0;\n    }\n}\n";

    [Fact]
    public void Replaced_ANestedPositionalRecordSentEndingInASemicolon_NamesEveryBodyMemberItDrops() =>
        Assert.Equal(
            "WARNING this replace drops 2 member(s) the type declared: Anchor.With(int), Anchor.Adjacent - a header alone, with no body and no ';', re-heads the type and keeps every member",
            DroppedDeclarations.Replaced(Target(), [SyntaxFactory.ParseMemberDeclaration("private readonly record struct Anchor(int First, int Count);")!]));

    [Fact]
    public void Replaced_ADeclarationThatKeepsEveryMemberAndAddsOne_WarnsNothing() =>
        Assert.Null(DroppedDeclarations.Replaced(
            Target(),
            [SyntaxFactory.ParseMemberDeclaration("private readonly record struct Anchor(int First, int Count)\n{\n    public Anchor With(int index) => this;\n\n    public bool Adjacent => Count > 0;\n\n    public int Extra => 1;\n}")!]));

    [Fact]
    public void Replaced_AMemberThatIsNotAType_WarnsNothing()
    {
        var method = Target().Members.OfType<MethodDeclarationSyntax>().Single();

        Assert.Null(DroppedDeclarations.Replaced(method, [SyntaxFactory.ParseMemberDeclaration("public Anchor With(int index) => default;")!]));
    }

    private static RecordDeclarationSyntax Target() =>
        CSharpSyntaxTree.ParseText(Nested).GetRoot().DescendantNodes().OfType<RecordDeclarationSyntax>().Single();

    [Fact]
    public void Replaced_AMemberWhoseSignatureChanged_IsNotReportedAsDropped() =>
        Assert.Null(DroppedDeclarations.Replaced(
            Target(),
            [SyntaxFactory.ParseMemberDeclaration("private readonly record struct Anchor(int First, int Count)\n{\n    public Anchor With(long index) => this;\n\n    public bool Adjacent => Count > 0;\n}")!]));
}
