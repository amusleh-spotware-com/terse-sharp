namespace TerseSharp.Core;

public static class XamlFiles
{
    public static IEnumerable<string> Enumerate(string root) => WorkspaceFiles.Enumerate(root, XamlDocument.IsXaml);

    public static bool IsExcluded(string file, string root) => WorkspaceFiles.IsExcluded(file, root);
}
