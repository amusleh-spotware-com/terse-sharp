using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class DocumentLookup
{
    public static Document? Find(LoadedWorkspace workspace, string path)
    {
        var full = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(workspace.Root, path));
        var name = Path.GetFileName(full);

        return workspace.Solution.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(document => Matches(document, full, name));
    }

    private static bool Matches(Document document, string full, string name) =>
        string.Equals(document.Name, name, PathBoundary.Comparison)
        && PathBoundary.SameFile(document.FilePath, full);
}
