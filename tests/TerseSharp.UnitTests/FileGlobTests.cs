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
    [InlineData("a/b/c/d/e/f/Order.cs", "**/**/**/Order.cs")]
    public void Matches_APatternThatCoversThePath_IsTrue(string path, string glob) =>
        Assert.True(FileGlob.Compile(glob).Matches(path), glob + " should match " + path);

    [Theory]
    [InlineData("src/Trading/Order.cs", "*.cs")]
    [InlineData("src/Trading/Order.cs", "**/Pricing/*.cs")]
    [InlineData("src/Trading/Order.cs", "**/Order.xaml")]
    [InlineData("src/Trading/OrderService.cs", "**/Order.cs")]
    [InlineData("Order.cs", "*.xaml")]
    [InlineData("Order.csx", "*.cs")]
    [InlineData("Order.cs", "**/src/*.cs")]
    public void Matches_APatternThatDoesNotCoverThePath_IsFalse(string path, string glob) =>
        Assert.False(FileGlob.Compile(glob).Matches(path), glob + " should not match " + path);

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

    [Theory]
    [InlineData("Order.cs", "*.*", true)]
    [InlineData("README", "*.*", false)]
    [InlineData("Order.cs", "Order?.cs", false)]
    [InlineData("Order1.cs", "Order?.cs", true)]
    [InlineData("README", "*.", false)]
    public void Matches_ADosWildcardQuirk_FollowsGlobRulesNotWin32(string path, string glob, bool expected) =>
        Assert.Equal(expected, FileGlob.Compile(glob).Matches(path));

    [Fact]
    public void Matches_ARegexMetacharacterInTheGlob_IsTakenLiterally()
    {
        Assert.True(FileGlob.Compile("Order(1).cs").Matches("Order(1).cs"));
        Assert.False(FileGlob.Compile("Order(1).cs").Matches("Orderx1y.cs"));
    }
}
