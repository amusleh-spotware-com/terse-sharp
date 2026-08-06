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
}
