using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;

namespace TerseSharp.Core;

public static class ProjectEvaluation
{
    private static Result<string> Evaluated(string root, string projectPath, string? name)
    {
        try
        {
            return Result.Ok(Rendered(root, projectPath, name));
        }
        catch (Exception exception) when (exception is InvalidProjectFileException or IOException or UnauthorizedAccessException)
        {
            return Result.Fail<string>(Errors.Invalid(
                "MSBuild could not evaluate " + PositionFormat.Relative(root, projectPath) + ": " + exception.Message,
                "build the project once, or restore it, so its imports resolve"));
        }
    }

    public static Task<Result<string>> Properties(string root, string projectPath, string? name, CancellationToken cancellationToken)
    {
        if (!File.Exists(projectPath))
            return Task.FromResult(Result.Fail<string>(Errors.DocumentNotFound(projectPath)));

        return Task.Run(() => Evaluated(root, projectPath, name), cancellationToken);
    }

    private static string Rendered(string root, string projectPath, string? name)
    {
        lock (ProjectGlobs.EvaluationGate)
        {
            using var collection = new ProjectCollection();
            var project = collection.LoadProject(projectPath);

            try
            {
                var response = new ResponseBuilder("project_properties", PositionFormat.Relative(root, projectPath));
                var kept = Declared(root, project, name);

                response.Summary(kept.Count, kept.Count, "properties", "name=");

                foreach (var line in kept)
                    response.Line(line);

                return response.ToString();
            }
            finally
            {
                collection.UnloadProject(project);
            }
        }
    }

    private static List<string> Declared(string root, Project project, string? name)
    {
        var declared = new List<string>();

        foreach (var property in project.Properties)
        {
            if (Wanted(root, property, name))
                declared.Add(property.Name + " = " + property.EvaluatedValue + "  " + PositionFormat.Relative(root, property.Xml!.ContainingProject.FullPath));
        }

        declared.Sort(StringComparer.Ordinal);

        return declared;
    }

    private static bool Wanted(string root, ProjectProperty property, string? name) =>
        property.Xml?.ContainingProject?.FullPath is { Length: > 0 } file
        && PathBoundary.Contains(root, file)
        && (name is not { Length: > 0 } || property.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string[]? Values(string projectPath, string[] names)
    {
        try
        {
            lock (ProjectGlobs.EvaluationGate)
            {
                using var collection = new ProjectCollection();
                var project = collection.LoadProject(projectPath);

                try
                {
                    return [.. names.Select(project.GetPropertyValue)];
                }
                finally
                {
                    collection.UnloadProject(project);
                }
            }
        }
        catch (Exception exception) when (exception is InvalidProjectFileException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
