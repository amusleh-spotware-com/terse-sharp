using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;

namespace TerseSharp.Core;

public static class ProjectGlobs
{
    public static bool? CompilesByGlob(string projectPath)
    {
        try
        {
            return Evaluated(projectPath);
        }
        catch (Exception exception) when (exception is InvalidProjectFileException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool Evaluated(string projectPath)
    {
        using var collection = new ProjectCollection();
        var project = collection.LoadProject(projectPath);

        try
        {
            return Sdk(project) && Enabled(project, "EnableDefaultItems") && Enabled(project, "EnableDefaultCompileItems");
        }
        finally
        {
            collection.UnloadProject(project);
        }
    }

    private static bool Sdk(Project project) =>
        string.Equals(project.GetPropertyValue("UsingMicrosoftNETSdk"), "true", StringComparison.OrdinalIgnoreCase);

    private static bool Enabled(Project project, string property) =>
        !string.Equals(project.GetPropertyValue(property), "false", StringComparison.OrdinalIgnoreCase);

    public static bool Memoized(string projectPath)
    {
        if (Stamp(projectPath) is not { } key)
            return false;

        if (Verdicts.TryGetValue(key, out var known))
            return known;

        var verdict = CompilesByGlob(projectPath) is true;

        if (Verdicts.Count >= MaxRememberedProjects)
            Verdicts.Clear();

        Verdicts[key] = verdict;

        return verdict;
    }

    private static GlobKey? Stamp(string path)
    {
        try
        {
            var file = new FileInfo(path);

            return new GlobKey(path, file.LastWriteTimeUtc, file.Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private const int MaxRememberedProjects = 256;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<GlobKey, bool> Verdicts = new();

    private readonly record struct GlobKey(string Path, DateTime LastWriteUtc, long Length);
}
