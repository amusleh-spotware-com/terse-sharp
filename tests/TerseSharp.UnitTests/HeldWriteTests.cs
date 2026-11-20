using TerseSharp.Core;
using TerseSharp.Server.Tools;

namespace TerseSharp.UnitTests;

public sealed class HeldWriteTests
{
    private static readonly RejectedEdit Held = new("r1", "root", "write_text", ["src/A.cs"], ["class A;"], [], null, [], Forced: [true], Overwrite: true);

    [Fact]
    public void Overwrite_ReplayedOntoACorrectedPath_IsNotInheritedFromTheToken() =>
        Assert.False(HeldWrite.Overwrite(false, "src/B.cs", Held));

    [Fact]
    public void Force_ReplayedOntoACorrectedPath_IsNotInheritedFromTheToken() =>
        Assert.False(HeldWrite.Force(false, "src/B.cs", Held));

    [Fact]
    public void ForceAndOverwrite_ReplayedOntoTheHeldPath_AreInheritedFromTheToken()
    {
        Assert.True(HeldWrite.Force(false, null, Held));
        Assert.True(HeldWrite.Overwrite(false, null, Held));
    }
}
