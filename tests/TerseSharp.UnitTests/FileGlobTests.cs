using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class FileGlobTests
{
    [Theory]
    [InlineData("Order.cs", "*.cs")]
    [InlineData("Order.cs", "Order.cs")]
    [InlineData("Order.cs", "O?der.cs")]
    [InlineData("src/Trading/Order.cs", "**/Order.cs")]
    [InlineData("src/Trading/Order.cs", "**/Trading/*.cs")]
    [InlineData("src/Trading/Order.cs", "src/**/Order.cs")]
    [InlineData("src/Trading/Order.cs", "src/Trading/Order.cs")]
    [InlineData("src/Trading/Order.cs", "src/**")]
    [InlineData("Order.cs", "**/Order.cs")]
    [InlineData("src\\Trading\\Order.cs", "**/Order.cs")]
    public void Matches_APatternThatCoversThePath_IsTrue(string path, string glob) =>
        Assert.True(FileGlob.Matches(path, glob), glob + " should match " + path);

    [Theory]
    [InlineData("src/Trading/Order.cs", "*.cs")]
    [InlineData("src/Trading/Order.cs", "**/Pricing/*.cs")]
    [InlineData("src/Trading/Order.cs", "**/Order.xaml")]
    [InlineData("src/Trading/OrderService.cs", "**/Order.cs")]
    [InlineData("Order.cs", "*.xaml")]
    public void Matches_APatternThatDoesNotCoverThePath_IsFalse(string path, string glob) =>
        Assert.False(FileGlob.Matches(path, glob), glob + " should not match " + path);

    [Theory]
    [InlineData("**/Apps/Automate2App/AutomateApp.xaml")]
    [InlineData("src/**/*.cs")]
    [InlineData("src\\Trading\\*.cs")]
    public void IsPathPattern_AGlobWithDirectories_IsTrue(string glob) =>
        Assert.True(FileGlob.IsPathPattern(glob));

    [Theory]
    [InlineData("*.cs")]
    [InlineData("AutomateApp.xaml")]
    [InlineData("*Tests.cs")]
    public void IsPathPattern_ABareFileGlob_IsFalse(string glob) =>
        Assert.False(FileGlob.IsPathPattern(glob));

    [Fact]
    public void Matches_ARegexMetacharacterInTheGlob_IsTakenLiterally()
    {
        Assert.True(FileGlob.Matches("Order(1).cs", "Order(1).cs"));
        Assert.False(FileGlob.Matches("Orderx1y.cs", "Order(1).cs"));
    }
}
