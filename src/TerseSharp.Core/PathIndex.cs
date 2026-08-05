namespace TerseSharp.Core;

public readonly record struct WorkspacePath(string FullPath, string RelativePath);

public sealed class PathIndex
{
    private readonly WorkspacePath[] paths;
    private readonly HashSet<string> lookup;

    private PathIndex(WorkspacePath[] paths)
    {
        this.paths = paths;
        lookup = new HashSet<string>(paths.Length, StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
            lookup.Add(path.FullPath);
    }

    public int Count => paths.Length;

    public ReadOnlySpan<WorkspacePath> Paths => paths;

    public bool Contains(string fullPath) => lookup.Contains(fullPath);

    public static PathIndex Build(string root)
    {
        var prefix = root.Length + (Path.EndsInDirectorySeparator(root) ? 0 : 1);
        var built = new List<WorkspacePath>(4096);

        foreach (var file in Walk(root))
            built.Add(new WorkspacePath(file, file[prefix..]));

        return new PathIndex([.. built]);
    }

    private static IEnumerable<string> Walk(string root)
    {
        var pending = new Stack<string>();

        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var child in Directories(directory))
                pending.Push(child);

            foreach (var file in Entries(directory))
                yield return file;
        }
    }

    private static string[] Entries(string directory)
    {
        try
        {
            return Directory.GetFiles(directory);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> Directories(string directory) =>
        Subdirectories(directory).Where(WorkspaceFiles.Traversable);

    private static string[] Subdirectories(string directory)
    {
        try
        {
            return Directory.GetDirectories(directory);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
