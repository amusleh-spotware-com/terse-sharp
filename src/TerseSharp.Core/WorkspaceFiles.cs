namespace TerseSharp.Core;

public static class WorkspaceFiles
{
    private static readonly string[] Excluded = ["bin", "obj", ".git", ".claude", "node_modules"];

    public static IEnumerable<string> Enumerate(string root, Func<string, bool> include) => Walk(root, include);

    public static bool IsExcluded(string file, string root) => Path
        .GetRelativePath(root, file)
        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(segment => Excluded.Contains(segment, StringComparer.OrdinalIgnoreCase));

    private static IEnumerable<string> Walk(string directory, Func<string, bool> include)
    {
        foreach (var file in Entries(directory, Directory.EnumerateFiles).Where(include))
            yield return file;

        foreach (var child in Entries(directory, Directory.EnumerateDirectories).Where(Traversable))
        {
            foreach (var file in Walk(child, include))
                yield return file;
        }
    }

    private static bool Traversable(string directory) =>
        !Excluded.Contains(Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase) && !IsLink(directory);

    private static bool IsLink(string directory)
    {
        try
        {
            return File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string[] Entries(string directory, Func<string, IEnumerable<string>> enumerate)
    {
        try
        {
            return [.. enumerate(directory)];
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
