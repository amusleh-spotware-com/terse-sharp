using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class PathIndexTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("terse-path-index-");

    public void Dispose() => directory.Delete(recursive: true);

    [Fact]
    public void Build_ListsEveryFileRelativeToTheRoot()
    {
        Write("Order.cs");
        Write(Path.Combine("Views", "OrderView.xaml"));

        var relative = Relative();

        Assert.Contains("Order.cs", relative);
        Assert.Contains(Path.Combine("Views", "OrderView.xaml"), relative);
    }

    [Fact]
    public void Build_SkipsExcludedDirectories()
    {
        Write(Path.Combine("bin", "Debug", "Order.dll"));
        Write(Path.Combine("obj", "Order.cs"));
        Write(Path.Combine(".git", "HEAD"));
        Write("Order.cs");

        Assert.Equal(["Order.cs"], Relative());
    }

    [Fact]
    public void Build_OnAnEmptyRoot_ReportsNoFiles() =>
        Assert.Equal(0, PathIndex.Build(directory.FullName).Count);

    [Fact]
    public void Build_KeepsTheFullPathAndTheRelativePathInStep()
    {
        Write(Path.Combine("Views", "OrderView.xaml"));

        var path = PathIndex.Build(directory.FullName).Paths[0];

        Assert.Equal(Path.Combine(directory.FullName, path.RelativePath), path.FullPath);
    }

    private string[] Relative()
    {
        var index = PathIndex.Build(directory.FullName);
        var relative = new string[index.Count];

        for (var position = 0; position < relative.Length; position++)
            relative[position] = index.Paths[position].RelativePath;

        return relative;
    }

    private void Write(string relativePath)
    {
        var full = Path.Combine(directory.FullName, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
    }

    [Fact]
    public void Contains_KnowsTheFilesItListedAndNothingElse()
    {
        Write("Order.cs");

        var index = PathIndex.Build(directory.FullName);

        Assert.True(index.Contains(Path.Combine(directory.FullName, "Order.cs")));
        Assert.False(index.Contains(Path.Combine(directory.FullName, "Absent.cs")));
    }
}
