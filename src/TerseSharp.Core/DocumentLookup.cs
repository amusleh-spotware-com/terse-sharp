using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class DocumentLookup
{
    public static Document? Find(LoadedWorkspace workspace, string path)
    {
        var full = Path.GetFullPath(path);

        return workspace.Solution.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(document => Matches(document, full, path));
    }

    private static bool Matches(Document document, string full, string original) =>
        document.FilePath is { } filePath
        && (filePath.Equals(full, StringComparison.OrdinalIgnoreCase)
            || filePath.EndsWith(original.Replace('/', Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase));
}
