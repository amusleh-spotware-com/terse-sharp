using System.Xml.Linq;

namespace TerseSharp.Core;

public static class SolutionFile
{
    public static bool IsXml(string path) =>
        Path.GetExtension(path).Equals(".slnx", StringComparison.OrdinalIgnoreCase);

    public static async Task<Result<string>> AddProject(string solutionPath, string projectPath, bool dryRun, bool verbose, CancellationToken cancellationToken)
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

        if ((await ProjectsAsync(solutionPath, cancellationToken).ConfigureAwait(false)).Contains(relative, StringComparer.OrdinalIgnoreCase))
            return Result.Fail<string>(Errors.Invalid($"'{relative}' is already in the solution", "nothing to add"));

        var document = await LoadAsync(solutionPath, cancellationToken).ConfigureAwait(false);

        document.Root!.Add(new XElement("Project", new XAttribute("Path", relative)));

        return await Write(solutionPath, document, dryRun, verbose, "solution_add_project", relative).ConfigureAwait(false);
    }

    public static async Task<Result<string>> RemoveProject(string solutionPath, string projectPath, bool dryRun, bool verbose, CancellationToken cancellationToken)
    {
        if (!IsXml(solutionPath))
            return Result.Fail<string>(Unsupported(solutionPath, "remove"));

        var relative = Relative(solutionPath, projectPath);
        var document = await LoadAsync(solutionPath, cancellationToken).ConfigureAwait(false);
        var element = document.Descendants("Project").FirstOrDefault(candidate => Matches(candidate, relative));

        if (element is null)
            return Result.Fail<string>(Errors.Invalid($"'{relative}' is not in the solution", "check solution_projects"));

        element.Remove();

        return await Write(solutionPath, document, dryRun, verbose, "solution_remove_project", relative).ConfigureAwait(false);
    }

    private static bool Matches(XElement element, string relative) =>
        Normalize(element.Attribute("Path")?.Value ?? string.Empty)
            .Equals(Normalize(relative), StringComparison.OrdinalIgnoreCase);

    private static bool IsProjectFile(string path) =>
        Path.GetExtension(path) is ".csproj" or ".fsproj" or ".vbproj";

    private static async Task<Result<string>> Write(
        string solutionPath,
        XDocument document,
        bool dryRun,
        bool verbose,
        string tool,
        string relative)
    {
        var before = await File.ReadAllTextAsync(solutionPath).ConfigureAwait(false);
        var after = document.ToString() + Environment.NewLine;

        if (!dryRun)
            await AtomicWrite.TextAsync(solutionPath, after).ConfigureAwait(false);

        var response = new ResponseBuilder(tool, relative).Verbose(verbose);

        if (!dryRun && !verbose)
        {
            return Result.Ok(response.Line(string.Create(
                CultureInfo.InvariantCulture,
                $"{Path.GetFileName(solutionPath.AsSpan())}  changedLines={UnifiedDiff.ChangedLines(before, after)}")).ToString());
        }

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

    private static string? ClassicPath(string line)
    {
        var text = line.AsSpan();
        var field = 0;

        foreach (var range in text.Split('"'))
        {
            if (field++ is not 5)
                continue;

            var candidate = text[range];

            return candidate.Contains('.') ? new string(candidate) : null;
        }

        return null;
    }

    public static bool IsSolutionFile(ReadOnlySpan<char> path) => Path.GetExtension(path) switch
    {
        var extension when extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) => true,
        var extension when extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) => true,
        _ => false,
    };

    public static async Task<Result<string>> RenderAsync(string solutionPath, bool echoPath, CancellationToken cancellationToken)
    {
        if (!IsSolutionFile(solutionPath))
        {
            return Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{solutionPath}' is not a solution file this tool can read"),
                "pass a path ending in .slnx or .sln; a .slnf solution filter is JSON and is not parsed yet, so it would answer 0 projects"));
        }

        if (!File.Exists(solutionPath))
        {
            return Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{solutionPath}' does not exist"),
                "pass an existing .slnx or .sln; a relative path is resolved against the server's working directory, so prefer an absolute one"));
        }

        var projects = await ProjectsAsync(solutionPath, cancellationToken).ConfigureAwait(false);
        var response = new ResponseBuilder("solution_projects", solutionPath);

        response.Summary(projects.Count, projects.Count, "projects");

        if (echoPath)
            response.Note("read  " + solutionPath);

        foreach (var project in projects)
            response.Line(project);

        return Result.Ok(response.ToString());
    }

    public static async Task<IReadOnlyList<string>> ProjectsAsync(string solutionPath, CancellationToken cancellationToken) =>
        IsXml(solutionPath)
            ? await XmlProjectsAsync(solutionPath, cancellationToken).ConfigureAwait(false)
            : await ClassicProjectsAsync(solutionPath, cancellationToken).ConfigureAwait(false);

    private static async Task<XDocument> LoadAsync(string solutionPath, CancellationToken cancellationToken)
    {
        var stream = new FileStream(
            solutionPath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });

        await using (stream.ConfigureAwait(false))
            return await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string[]> XmlProjectsAsync(string solutionPath, CancellationToken cancellationToken)
    {
        var document = await LoadAsync(solutionPath, cancellationToken).ConfigureAwait(false);

        return [.. document
        .Descendants("Project")
        .Select(element => element.Attribute("Path")?.Value)
        .OfType<string>()];
    }

    private static async Task<string[]> ClassicProjectsAsync(string solutionPath, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(solutionPath, cancellationToken).ConfigureAwait(false);

        return [.. lines
        .Where(line => line.StartsWith("Project(", StringComparison.Ordinal))
        .Select(ClassicPath)
        .OfType<string>()];
    }
}
