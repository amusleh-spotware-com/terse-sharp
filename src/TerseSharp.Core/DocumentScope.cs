using System.Buffers;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class DocumentScope
{
    public static DocumentId[] Select(LoadedWorkspace workspace, string? path, bool changedOnly)
    {
        var targets = Targets(workspace, path);

        return changedOnly ? [.. targets.Where(id => Touched(workspace, id))] : targets;
    }

    public static IEnumerable<Document> Editable(LoadedWorkspace workspace) =>
        Sources(workspace).Where(document => !GeneratedCode.IsGenerated(workspace.Root, document.FilePath));

    private static bool Touched(LoadedWorkspace workspace, DocumentId id)
    {
        if (workspace.Solution.GetDocument(id)?.FilePath is not { Length: > 0 } file)
            return false;

        try
        {
            return File.GetLastWriteTimeUtc(file) > workspace.ChangedSinceUtc.UtcDateTime;
        }
        catch (IOException)
        {
            return false;
        }
    }
    private static DocumentId[] Targets(LoadedWorkspace workspace, string? path)
    {
        if (path is null)
            return [.. Editable(workspace).Select(document => document.Id)];

        if (string.IsNullOrWhiteSpace(path))
            return [];

        if (IsGlob(path))
            return [.. Matching(workspace, FileGlob.Compile(path)).Select(document => document.Id)];

        if (DocumentLookup.Find(workspace, path) is { } document)
            return [document.Id];

        return [.. UnderDirectory(workspace, path)];
    }

    private static bool IsGlob(string path) =>
            path.AsSpan().IndexOfAny(GlobCharacters) >= 0;

    private static IEnumerable<Document> Sources(LoadedWorkspace workspace) =>
        workspace.Solution.Projects.SelectMany(project => project.Documents);

    private static bool Matches(string root, Document document, FileGlob glob) =>
        document.FilePath is { Length: > 0 } file && glob.MatchesFile(root, file);

    private static IEnumerable<DocumentId> UnderDirectory(LoadedWorkspace workspace, string path)
    {
        var resolved = PathGuard.Resolve(workspace, path);

        return resolved.IsOk && Directory.Exists(resolved.Value)
            ? Editable(workspace).Where(document => Inside(resolved.Value!, document)).Select(document => document.Id)
            : [];
    }

    private static bool Inside(string directory, Document document) =>
        document.FilePath is { Length: > 0 } file && PathBoundary.Contains(directory, file);

    private static IEnumerable<Document> Matching(LoadedWorkspace workspace, FileGlob glob) =>
        Editable(workspace).Where(document => Matches(workspace.Root, document, glob));

    private static readonly SearchValues<char> GlobCharacters = SearchValues.Create("*?{");
}
