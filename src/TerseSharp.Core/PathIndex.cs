using System.IO.Enumeration;

namespace TerseSharp.Core;

public readonly record struct WorkspacePath(string FullPath, string RelativePath);

public sealed class PathIndex
{
    private static readonly EnumerationOptions Shallow = new() { IgnoreInaccessible = false, AttributesToSkip = 0 };
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
        var pending = new Stack<string>([root]);
        var files = new List<string>();

        while (pending.TryPop(out var directory))
        {
            Read(directory, pending, files);

            if (WorkspaceFiles.HoldsSessionState(directory))
                continue;

            foreach (var file in files)
                yield return file;
        }
    }

    private static void Read(string directory, Stack<string> pending, List<string> files)
    {
        files.Clear();

        try
        {
            foreach (var (path, isDirectory) in Entries(directory))
            {
                if (isDirectory)
                    pending.Push(path);
                else
                    files.Add(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            files.Clear();
        }
    }

    private static FileSystemEnumerable<(string Path, bool IsDirectory)> Entries(string directory) =>
        new(directory, static (ref entry) => (entry.ToSpecifiedFullPath(), entry.IsDirectory), Shallow)
        {
            ShouldIncludePredicate = static (ref entry) => !entry.IsDirectory || WorkspaceFiles.Traversable(ref entry),
        };
}
