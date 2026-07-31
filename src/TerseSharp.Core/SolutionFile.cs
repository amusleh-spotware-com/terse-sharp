using System.Xml.Linq;

namespace TerseSharp.Core;

public static class SolutionFile
{
    public static bool IsXml(string path) =>
        Path.GetExtension(path).Equals(".slnx", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Projects(string solutionPath) =>
        IsXml(solutionPath) ? XmlProjects(solutionPath) : ClassicProjects(solutionPath);

    public static Result<string> AddProject(string solutionPath, string projectPath, bool dryRun)
    {
        if (!IsXml(solutionPath))
            return Result.Fail<string>(Unsupported(solutionPath, "add"));

        if (string.IsNullOrWhiteSpace(projectPath))
            return Result.Fail<string>(Errors.Blank("project"));

        if (!IsProjectFile(projectPath))
        {
            return Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{projectPath}' is not a project file"),
                "pass a path ending in .csproj, .fsproj or .vbproj"));
        }

        var relative = Relative(solutionPath, projectPath);

        if (Projects(solutionPath).Contains(relative, StringComparer.OrdinalIgnoreCase))
            return Result.Fail<string>(Errors.Invalid($"'{relative}' is already in the solution", "nothing to add"));

        var document = XDocument.Load(solutionPath);

        document.Root!.Add(new XElement("Project", new XAttribute("Path", relative)));

        return Write(solutionPath, document, dryRun, "solution_add_project", relative);
    }

    public static Result<string> RemoveProject(string solutionPath, string projectPath, bool dryRun)
    {
        if (!IsXml(solutionPath))
            return Result.Fail<string>(Unsupported(solutionPath, "remove"));

        var relative = Relative(solutionPath, projectPath);
        var document = XDocument.Load(solutionPath);
        var element = document.Descendants("Project").FirstOrDefault(candidate => Matches(candidate, relative));

        if (element is null)
            return Result.Fail<string>(Errors.Invalid($"'{relative}' is not in the solution", "check solution_projects"));

        element.Remove();

        return Write(solutionPath, document, dryRun, "solution_remove_project", relative);
    }

    private static bool Matches(XElement element, string relative) =>
        Normalize(element.Attribute("Path")?.Value ?? string.Empty)
            .Equals(Normalize(relative), StringComparison.OrdinalIgnoreCase);

    private static bool IsProjectFile(string path) =>
        Path.GetExtension(path) is ".csproj" or ".fsproj" or ".vbproj";

    private static Result<string> Write(string solutionPath, XDocument document, bool dryRun, string tool, string relative)
    {
        var before = File.ReadAllText(solutionPath);
        var after = document.ToString() + Environment.NewLine;

        if (!dryRun)
            AtomicWrite.Text(solutionPath, after);

        var response = new ResponseBuilder(tool, relative);

        response.Summary(1, 1, "files changed");
        response.Note(dryRun ? "dryRun" : "applied");
        response.Line(UnifiedDiff.Between(solutionPath, before, after));

        return Result.Ok(response.ToString());
    }

    private static TerseError Unsupported(string solutionPath, string operation) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"cannot {operation} a project in '{Path.GetExtension(solutionPath)}' solutions"),
        "TerseSharp edits .slnx solutions; convert with: dotnet sln migrate");

    private static string Relative(string solutionPath, string projectPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        var full = Path.IsPathRooted(projectPath) ? projectPath : Path.Combine(directory, projectPath);

        return Path.GetRelativePath(directory, Path.GetFullPath(full)).Replace('\\', '/');
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('.', '/');

    private static string[] XmlProjects(string solutionPath) =>
        [.. XDocument.Load(solutionPath)
            .Descendants("Project")
            .Select(element => element.Attribute("Path")?.Value)
            .OfType<string>()];

    private static string[] ClassicProjects(string solutionPath) =>
        [.. File.ReadLines(solutionPath)
            .Where(line => line.StartsWith("Project(", StringComparison.Ordinal))
            .Select(ClassicPath)
            .OfType<string>()];

    private static string? ClassicPath(string line)
    {
        var parts = line.Split('"');

        return parts.Length > 5 && parts[5].Contains('.', StringComparison.Ordinal) ? parts[5] : null;
    }
}
