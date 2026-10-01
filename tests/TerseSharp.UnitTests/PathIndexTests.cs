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
    public void Build_UnderClaude_KeepsTheAuthoredFoldersAndDropsTheSessionState()
    {
        Write(Path.Combine(".claude", "settings.local.json"));
        Write(Path.Combine(".claude", "commands", "review.md"));
        Write(Path.Combine(".claude", "worktrees", "agent-1", "Order.cs"));
        Write("Order.cs");

        Assert.Equal(
            new SortedSet<string>([Path.Combine(".claude", "commands", "review.md"), "Order.cs"], StringComparer.Ordinal),
            new SortedSet<string>(Relative(), StringComparer.Ordinal));
    }

    [Fact]
    public void Build_DoesNotDescendIntoASymlinkedDirectory()
    {
        Write(Path.Combine("Views", "OrderView.xaml"));
        LinkDirectoryOrSkip(Path.Combine(directory.FullName, "loop"), directory.FullName);

        Assert.Equal([Path.Combine("Views", "OrderView.xaml")], Relative());
    }

    [Fact]
    public void Build_ListsFilesInTheOrderOfTheTwoPassWalkItReplaced()
    {
        Write("Root.cs");
        Write(Path.Combine("Alpha", "One.cs"));
        Write(Path.Combine("Alpha", "Inner", "Two.cs"));
        Write(Path.Combine("Beta", "Three.cs"));
        Write(Path.Combine("Beta", "Four.cs"));
        Write(Path.Combine("bin", "Skipped.dll"));

        Assert.Equal(TwoPassWalk(directory.FullName), Relative());
    }

    private static string[] TwoPassWalk(string root)
    {
        var pending = new Stack<string>([root]);
        var listed = new List<string>();

        while (pending.TryPop(out var current))
        {
            foreach (var child in Directory.GetDirectories(current).Where(WorkspaceFiles.Traversable))
                pending.Push(child);

            listed.AddRange(Directory.GetFiles(current).Select(file => Path.GetRelativePath(root, file)));
        }

        return [.. listed];
    }

    private static void LinkDirectoryOrSkip(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("this account may not create a directory symbolic link: " + exception.Message);
        }
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
