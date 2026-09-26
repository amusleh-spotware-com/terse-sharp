using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class SymbolDerivationTests
{
    private const string Source = """
        public interface IShape { int Area(); }
        public abstract class Base { public abstract int Abstract(); public virtual int Virtual() => 1; public int Plain() => 1; }
        public class Middle : Base { public override int Abstract() => 2; public sealed override int Virtual() => 2; }
        public sealed class Leaf : Middle { public override int Abstract() => 3; }
        public struct Value { public override string ToString() => ""; }
        """;

    [Theory]
    [InlineData("IShape", "Area", true)]
    [InlineData("Base", "Abstract", true)]
    [InlineData("Base", "Virtual", true)]
    [InlineData("Base", "Plain", false)]
    [InlineData("Middle", "Abstract", true)]
    [InlineData("Middle", "Virtual", false)]
    [InlineData("Leaf", "Abstract", false)]
    [InlineData("Value", "ToString", false)]
    public void CanBeOverridden_IsTrueOnlyForAMemberSomethingCouldStillOverride(string type, string member, bool overridable) =>
        Assert.Equal(overridable, SymbolDerivation.CanBeOverridden(Member(type, member)));

    private static ISymbol Member(string type, string member)
    {
        var compilation = CSharpCompilation.Create(
            "derivation",
            [CSharpSyntaxTree.ParseText(Source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        return compilation.GetTypeByMetadataName(type)!.GetMembers(member)[0];
    }
}
