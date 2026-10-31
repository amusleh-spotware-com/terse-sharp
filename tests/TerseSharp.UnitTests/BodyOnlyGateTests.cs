using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class BodyOnlyGateTests
{
    [Theory]
    [InlineData("class C { int M() { return 1; } }", "class C { int M() { return 2; } }", true)]
    [InlineData("class C { int P => 1; }", "class C { int P => 2; }", true)]
    [InlineData("class C { int P { get { return 1; } } }", "class C { int P { get { return 2; } } }", true)]
    [InlineData("class C { C() { } }", "class C { C() { var x = 1; } }", true)]
    [InlineData("class C { System.Collections.Generic.IEnumerable<int> M() { return []; } }", "class C { System.Collections.Generic.IEnumerable<int> M() { yield return 1; } }", true)]
    [InlineData("class C { int f = 1; }", "class C { int f = 2; }", true)]
    [InlineData("class C { int M() => 1; }", "class C { long M() => 1; }", false)]
    [InlineData("class C { void M(int a = 1) { } }", "class C { void M(int a = 2) { } }", false)]
    [InlineData("class C { const int K = 1; }", "class C { const int K = 2; }", false)]
    [InlineData("class C { int P { get; } = 1; }", "class C { int P { get; } = 2; }", false)]
    [InlineData("class C { void M() { } }", "class C { [System.Obsolete] void M() { } }", false)]
    [InlineData("class C { void M() { } }", "class C { async void M() { } }", false)]
    [InlineData("interface I { void M(); }", "interface I { void M() { } }", false)]
    [InlineData("enum E { A = 1 }", "enum E { A = 2 }", false)]
    [InlineData("class B { public B(int a) { } } class C : B { C() : base(1) { } }", "class B { public B(int a) { } } class C : B { C() : base(2) { } }", false)]
    [InlineData("System.Console.WriteLine(1);", "System.Console.WriteLine(2);", false)]
    [InlineData("class C { void M() { } }", "class C { void M() {\n#nullable enable\n} }", false)]
    [InlineData("#nullable disable\nclass C { string M() => \"\"; }", "class C { string M() => \"\"; }", false)]
    [InlineData("struct S { int P { get { return 1; } } }", "struct S { int P { get { return field; } } }", false)]
    [InlineData("record struct S(object o) { int M() => 1; }", "record struct S(object o) { int M() => o.GetHashCode(); }", false)]
    public void BodyOnly_ComparingTwoVersionsOfADocument_NarrowsOnlyWhenNoDeclarationCouldChange(string before, string after, bool expected) =>
        Assert.Equal(expected, EditGate.BodyOnly(Root(before), Root(after)));

    private static SyntaxNode Root(string text) =>
        CSharpSyntaxTree.ParseText(text, cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken);
}
