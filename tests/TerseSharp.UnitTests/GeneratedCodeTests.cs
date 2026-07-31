using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class GeneratedCodeTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "terse-generated-root"));

    [Theory]
    [InlineData("obj/Debug/net10.0/Thing.GlobalUsings.g.cs")]
    [InlineData("obj/Debug/net10.0/Thing.AssemblyInfo.cs")]
    [InlineData("obj/Debug/net10.0/.NETCoreApp,Version=v10.0.AssemblyAttributes.cs")]
    [InlineData("bin/Debug/net10.0/Leftover.cs")]
    [InlineData("src/Thing.designer.cs")]
    [InlineData("src/Thing.generated.cs")]
    public void IsGenerated_ForBuildOutput_IsTrue(string relativePath) =>
        Assert.True(GeneratedCode.IsGenerated(Root, Path.Combine(Root, relativePath)));

    [Theory]
    [InlineData("src/OrderService.cs")]
    [InlineData("src/Objects/Order.cs")]
    [InlineData("src/Binder.cs")]
    public void IsGenerated_ForHandWrittenSource_IsFalse(string relativePath) =>
        Assert.False(GeneratedCode.IsGenerated(Root, Path.Combine(Root, relativePath)));

    [Fact]
    public void IsGenerated_ForAPathOutsideTheRoot_IsFalse() =>
        Assert.False(GeneratedCode.IsGenerated(Root, Path.Combine(Path.GetTempPath(), "elsewhere", "obj", "Thing.cs")));

    [Fact]
    public void IsGenerated_WhenTheRootItselfSitsUnderAnObjFolder_LooksAtTheRelativePathOnly()
    {
        var nested = Path.Combine(Root, "obj", "checkout");

        Assert.False(GeneratedCode.IsGenerated(nested, Path.Combine(nested, "src", "Order.cs")));
    }

    [Fact]
    public void IsGenerated_ForNoPath_IsFalse() => Assert.False(GeneratedCode.IsGenerated(Root, null));
}
