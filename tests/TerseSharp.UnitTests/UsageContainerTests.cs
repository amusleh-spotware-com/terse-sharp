using Microsoft.CodeAnalysis.CSharp;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class UsageContainerTests
{
    [Theory]
    [InlineData("class Holder { void Run() { var value = TARGET; } }", "Holder.Run")]
    [InlineData("class Holder { int Field = TARGET; }", "Holder.Field")]
    [InlineData("class Holder { int Value => TARGET; }", "Holder.Value")]
    [InlineData("class Holder { Holder() { var value = TARGET; } }", "Holder.Holder")]
    [InlineData("class Holder { int this[int index] => TARGET; }", "Holder.this[]")]
    [InlineData("enum Kind { First = TARGET }", "Kind.First")]
    [InlineData("class Holder { void Run() { void Local() { var value = TARGET; } } }", "Holder.Run")]
    [InlineData("class Holder { void Run() { var take = () => TARGET; } }", "Holder.Run")]
    [InlineData("class Outer { class Inner { void Run() { var value = TARGET; } } }", "Inner.Run")]
    public void Of_NamesTheDeclarationTheUsageSitsIn(string source, string expected) =>
        Assert.Equal(expected, Container(source));

    [Fact]
    public void Of_ForAUsageInATopLevelStatement_ReportsNoContainerRatherThanAPlaceholder() =>
        Assert.Null(Container("var value = TARGET;"));

    [Fact]
    public void Of_WithNoSyntaxTree_ReportsNoContainer() =>
        Assert.Null(UsageContainer.Of(null, new Microsoft.CodeAnalysis.Text.TextSpan(0, 1)));

    private static string? Container(string source)
    {
        var marked = source.Replace("TARGET", "Marker", StringComparison.Ordinal);
        var root = CSharpSyntaxTree.ParseText(marked).GetRoot();
        var span = new Microsoft.CodeAnalysis.Text.TextSpan(marked.IndexOf("Marker", StringComparison.Ordinal), 6);

        return UsageContainer.Of(root, span);
    }
}
