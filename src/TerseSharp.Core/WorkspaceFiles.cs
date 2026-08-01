namespace TerseSharp.Core;

public static class WorkspaceFiles
{
    private static readonly string[] ExcludedDirectories =
        ["bin", "obj", ".git", ".claude", "node_modules", ".vs", ".idea", "artifacts", "TestResults"];

    private static readonly string[] TemporaryExtensions = [".tmp", ".swp", ".swx", ".orig", ".rej"];

    public static IEnumerable<string> Enumerate(string root, Func<string, bool> include) => Walk(root, include);

    public static bool IsExcludedDirectory(string name) =>
        ExcludedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static bool IsExcluded(string file, string root) => Path
        .GetRelativePath(root, file)
        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(IsExcludedDirectory);

    public static bool IsTemporary(string path)
    {
        var name = Path.GetFileName(path.AsSpan());

        return name.EndsWith('~')
            || name.StartsWith("~$", StringComparison.Ordinal)
            || name.StartsWith(".#", StringComparison.Ordinal)
            || Matches(Path.GetExtension(path.AsSpan()), TemporaryExtensions);
    }

    internal static bool Matches(ReadOnlySpan<char> extension, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (extension.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

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

    public static bool Traversable(string directory) =>
        !IsExcludedDirectory(Path.GetFileName(directory)) && !IsLink(directory);

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
