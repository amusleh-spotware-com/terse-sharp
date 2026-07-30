using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class PathBoundaryTests
{
    [Theory]
    [InlineData(@"C:\repo", @"C:\repo\src\File.cs", true)]
    [InlineData(@"C:\repo", @"C:\repo", true)]
    [InlineData(@"C:\repo\", @"C:\repo\src\File.cs", true)]
    [InlineData(@"C:\repo", @"C:\repoEvil\secrets.txt", false)]
    [InlineData(@"C:\repo", @"C:\repo-feature\src\File.cs", false)]
    [InlineData(@"C:\repo", @"C:\other\File.cs", false)]
    [InlineData(@"C:\repo", @"C:\repo\..\escaped.txt", false)]
    public void Contains_JudgesSiblingDirectoriesThatShareAPrefixAsOutside(string root, string candidate, bool expected) =>
        Assert.Equal(expected, PathBoundary.Contains(root, candidate));

    [Fact]
    public void Contains_IsCaseInsensitiveOnTheRoot() =>
        Assert.True(PathBoundary.Contains(@"C:\Repo", @"c:\repo\src\File.cs"));
}
