namespace TerseSharp.Core;

public static class WorkspaceDiscovery
{
    private static readonly string[] SolutionExtensions = [".slnx", ".sln", ".slnf"];

    public static string[] Find(string startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));

        while (directory is not null)
        {
            var solutions = SolutionsIn(directory);

            if (solutions.Length > 0)
                return solutions;

            var projects = directory.GetFiles("*.csproj").Select(file => file.FullName).ToArray();

            if (projects.Length > 0)
                return projects;

            directory = directory.Parent;
        }

        return [];
    }

    public static bool IsSolution(string path) =>
        SolutionExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string[] SolutionsIn(DirectoryInfo directory) =>
        [.. SolutionExtensions
            .SelectMany(extension => directory.GetFiles("*" + extension))
            .Select(file => file.FullName)];
}
