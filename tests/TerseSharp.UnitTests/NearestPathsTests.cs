using System.Globalization;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class NearestPathsTests
{
    [Fact]
    public async Task Around_AMissingDirectory_RanksTheNearestAncestorsFilesBySharedPrefix()
    {
        var root = Directory.CreateTempSubdirectory("terse-nearest-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "readme.md"), "a", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, "release.md"), "a", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, "release.txt"), "a", TestContext.Current.CancellationToken);

            var nearest = NearestPaths.Around(root, Path.Combine("docs", "releasing.md"));

            Assert.Equal(["release.md", "readme.md"], nearest);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Around_ADirectoryWithManyMatches_ShowsAtMostFive()
    {
        var root = Directory.CreateTempSubdirectory("terse-nearest-").FullName;
        try
        {
            for (var index = 0; index < 7; index++)
                await File.WriteAllTextAsync(Path.Combine(root, "note" + index.ToString(CultureInfo.InvariantCulture) + ".md"), "a", TestContext.Current.CancellationToken);

            Assert.Equal(NearestPaths.MaxShown, NearestPaths.Around(root, "notes.md").Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Around_APathOutsideTheRoot_AnswersNothing()
    {
        var root = Directory.CreateTempSubdirectory("terse-nearest-").FullName;
        try
        {
            Assert.Empty(NearestPaths.Around(root, Path.Combine("..", "elsewhere", "x.md")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
