using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

public static class DocumentPlacement
{
    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public static Solution Add(Solution solution, DocumentId id, string full, SourceText text) =>
        solution.AddDocument(id, Path.GetFileName(full), text, Folders(solution.GetProject(id.ProjectId), full), full);

    public static string[] Folders(Project? project, string full)
    {
        if (Path.GetDirectoryName(project?.FilePath ?? string.Empty) is not { Length: > 0 } root)
            return [];

        if (Path.GetDirectoryName(full) is not { Length: > 0 } directory || !PathBoundary.Contains(root, directory))
            return [];

        var relative = Path.GetRelativePath(root, directory);

        return relative is "." ? [] : relative.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
    }
}
