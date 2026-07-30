using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class PathBoundaryTests
{
    private static readonly string Anchor = OperatingSystem.IsWindows() ? @"C:\" : "/";

    [Fact]
    public void Contains_AFileUnderTheRoot_IsInside() =>
        Assert.True(PathBoundary.Contains(Under("repo"), Under("repo", "src", "File.cs")));

    [Fact]
    public void Contains_TheRootItself_IsInside() =>
        Assert.True(PathBoundary.Contains(Under("repo"), Under("repo")));

    [Fact]
    public void Contains_ATrailingSeparatorOnTheRoot_ChangesNothing() =>
        Assert.True(PathBoundary.Contains(Under("repo") + Path.DirectorySeparatorChar, Under("repo", "src", "File.cs")));

    [Fact]
    public void Contains_ASiblingWhoseNameExtendsTheRoot_IsOutside() =>
        Assert.False(PathBoundary.Contains(Under("repo"), Under("repoEvil", "secrets.txt")));

    [Fact]
    public void Contains_ASiblingWorktreeOfTheSameRepo_IsOutside() =>
        Assert.False(PathBoundary.Contains(Under("repo"), Under("repo-feature", "src", "File.cs")));

    [Fact]
    public void Contains_AnUnrelatedDirectory_IsOutside() =>
        Assert.False(PathBoundary.Contains(Under("repo"), Under("other", "File.cs")));

    [Fact]
    public void Contains_ATraversalThatEscapesTheRoot_IsOutside() =>
        Assert.False(PathBoundary.Contains(Under("repo"), Under("repo", "..", "escaped.txt")));

    [Fact]
    public void Contains_ADifferentlyCasedRoot_FollowsTheFileSystemSemantics()
    {
        var matched = PathBoundary.Contains(Under("Repo"), Under("repo", "src", "File.cs"));

        Assert.Equal(!OperatingSystem.IsLinux(), matched);
    }

    private static string Under(params string[] segments) => Path.Combine([Anchor, .. segments]);
}
