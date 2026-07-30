using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class DocumentLookup
{
    public static Document? Find(LoadedWorkspace workspace, string path)
    {
        var full = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(workspace.Root, path));

        return workspace.Solution.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(document => Matches(document, full));
    }

    private static bool Matches(Document document, string full) =>
        document.FilePath is { } filePath
        && Path.GetFullPath(filePath).Equals(full, StringComparison.OrdinalIgnoreCase);
}
