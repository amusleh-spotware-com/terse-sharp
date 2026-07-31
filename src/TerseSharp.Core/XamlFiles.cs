namespace TerseSharp.Core;

public static class XamlFiles
{
    private static readonly string[] Excluded = ["bin", "obj", ".git", "node_modules"];

    public static IEnumerable<string> Enumerate(string root) => Walk(root);

    private static IEnumerable<string> Walk(string directory)
    {
        foreach (var file in Entries(directory, Directory.EnumerateFiles).Where(XamlDocument.IsXaml))
            yield return file;

        foreach (var child in Entries(directory, Directory.EnumerateDirectories).Where(Traversable))
        {
            foreach (var file in Walk(child))
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
