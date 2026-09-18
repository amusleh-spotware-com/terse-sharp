using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class VisibleShapeFixesTests
{
    private const string Source = """
        public sealed class Sealed { protected int Kept() => 1; }
        public class Open { protected int Kept() => 1; }
        internal sealed class Hidden { public int Kept() => 1; }
        public class Exposed { public int Kept() => 1; }
        """;

    [Theory]
    [InlineData("Sealed", false)]
    [InlineData("Open", true)]
    [InlineData("Hidden", false)]
    [InlineData("Exposed", true)]
    public void Visible_FollowsWhetherAnythingOutsideTheAssemblyCanActuallyReachTheMember(string type, bool visible) =>
        Assert.Equal(visible, VisibleShapeFixes.Visible(Member(type)));

    private static ISymbol Member(string type)
    {
        var compilation = CSharpCompilation.Create(
            "visibility",
            [CSharpSyntaxTree.ParseText(Source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        return compilation.GetTypeByMetadataName(type)!.GetMembers("Kept")[0];
    }
}
