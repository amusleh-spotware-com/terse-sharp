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

    [Fact]
    public void Comparer_IsCaseSensitiveExactlyWhereTheFilesystemIs()
    {
        var expected = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        Assert.Same(expected, PathBoundary.Comparer);
        Assert.Equal(OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase, PathBoundary.Comparison);
        Assert.Equal(!OperatingSystem.IsLinux(), PathBoundary.Comparer.Equals("src/A.md", "src/a.md"));
    }

    [Fact]
    public void RealPath_ThroughALinkedAncestorSeveralLevelsUp_ResolvesToTheTargetSpelling()
    {
        var scratch = Directory.CreateTempSubdirectory("terse-realpath-");

        try
        {
            var target = Directory.CreateDirectory(Path.Combine(scratch.FullName, "target", "deep", "er"));
            var link = Path.Combine(scratch.FullName, "link");

            try
            {
                Directory.CreateSymbolicLink(link, Path.Combine(scratch.FullName, "target"));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Assert.Skip("this account may not create a directory symbolic link: " + exception.Message);
            }

            var real = PathBoundary.RealPath(Path.Combine(link, "deep", "er", "App.slnx"));

            Assert.Equal(Path.Combine(PathBoundary.RealPath(target.FullName), "App.slnx"), real, ignoreCase: !OperatingSystem.IsLinux());
            Assert.DoesNotContain(Path.DirectorySeparatorChar + "link" + Path.DirectorySeparatorChar, real, StringComparison.Ordinal);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    [Fact]
    public void RealPath_ForASymlinkedSolutionFile_KeepsTheLinksOwnDirectoryBecauseMsBuildResolvesProjectsFromThere()
    {
        var scratch = Directory.CreateTempSubdirectory("terse-realpath-leaf-");

        try
        {
            var original = Path.Combine(Directory.CreateDirectory(Path.Combine(scratch.FullName, "a")).FullName, "App.slnx");
            var linked = Path.Combine(Directory.CreateDirectory(Path.Combine(scratch.FullName, "b")).FullName, "App.slnx");

            File.WriteAllText(original, "<Solution />");
            CreateFileLinkOrSkip(linked, original);

            Assert.Equal(Path.Combine(PathBoundary.RealDirectory(Path.GetDirectoryName(linked)!), "App.slnx"), PathBoundary.RealPath(linked), ignoreCase: !OperatingSystem.IsLinux());
            Assert.NotEqual(PathBoundary.RealPath(original), PathBoundary.RealPath(linked), StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    private static void CreateFileLinkOrSkip(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("this account may not create a file symbolic link: " + exception.Message);
        }
    }
}
