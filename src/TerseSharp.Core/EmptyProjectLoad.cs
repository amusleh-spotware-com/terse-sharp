using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class EmptyProjectLoad
{
    public static string[] Failures(Solution solution) => Failures(solution, ProjectGlobs.Memoized);

    public static string Message(string projectPath) =>
        "Project '" + projectPath + "' loaded with no documents, so nothing it declares can be resolved; its design-time build produced no compile items.";

    public static string[] Failures(Solution solution, Func<string, bool> globsSources)
    {
        var empty = new List<string>();

        foreach (var project in solution.Projects)
        {
            if (project.FilePath is { Length: > 0 } path && !project.Documents.Any() && globsSources(path))
                empty.Add(Message(path));
        }

        return [.. empty];
    }
}
