using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class PositionFormatTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "terse-position-root"));

    [Fact]
    public void Relative_ForAFileInsideTheRoot_DropsTheRoot() =>
        Assert.Equal(
            Path.Combine("src", "Order.cs"),
            PositionFormat.Relative(Root, Path.Combine(Root, "src", "Order.cs")));

    [Fact]
    public void Relative_ForAFileOutsideTheRoot_KeepsTheFullPath()
    {
        var outside = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "terse-elsewhere", "Order.cs"));

        Assert.Equal(outside, PositionFormat.Relative(Root, outside));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Relative_WithoutAPath_RendersADash(string? path) =>
        Assert.Equal("-", PositionFormat.Relative(Root, path));
}
