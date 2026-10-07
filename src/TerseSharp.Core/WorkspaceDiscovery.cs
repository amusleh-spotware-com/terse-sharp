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

    public static string Discover(string root, int maxResults)
    {
        var full = Path.GetFullPath(root);
        var found = Under(full).ToArray();
        var response = new ResponseBuilder("load_workspace", "discover " + full);

        response.Summary(ResultCap.Shown(found.Length, maxResults), found.Length, "candidates", "a narrower directory");

        if (found.Length is 0)
            response.Line("no .slnx, .sln, .slnf or .csproj under this directory");

        foreach (var candidate in found.Capped(maxResults))
            response.Line(PositionFormat.Relative(full, candidate));

        return response.ToString();
    }

    private static IEnumerable<string> Under(string root) => WorkspaceFiles
            .Enumerate(root, IsWorkspaceFile)
            .OrderBy(file => file.AsSpan().Count(Path.DirectorySeparatorChar))
            .ThenBy(file => file, StringComparer.Ordinal);

    private static bool IsWorkspaceFile(string file) =>
            IsSolution(file) || Path.GetExtension(file.AsSpan()).Equals(".csproj", StringComparison.OrdinalIgnoreCase);

    public static Result<string> SolutionIn(string directory)
    {
        var candidates = DirectCandidates(new DirectoryInfo(Path.GetFullPath(directory)));

        return candidates switch
        {
            [var only] => Result.Ok(only),
            [] => Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{directory}' is a directory with no .slnx, .sln, .slnf or .csproj directly in it"),
                string.Create(CultureInfo.InvariantCulture, $"load_workspace path={directory} discover=true lists the candidates below it"))),
            _ => Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{directory}' is a directory holding {candidates.Length} candidates"),
                "pass one of them as path=: " + string.Join(" | ", candidates.Select(candidate => Path.Join(directory.AsSpan(), Path.GetFileName(candidate.AsSpan())))))),
        };
    }

    private static string[] DirectCandidates(DirectoryInfo directory) => SolutionsIn(directory) switch
    {
        [] => [.. directory.GetFiles("*.csproj").Select(file => file.FullName)],
        var solutions when Array.FindAll(solutions, IsWholeSolution) is { Length: > 0 } whole => whole,
        var solutions => solutions,
    };

    private static bool IsWholeSolution(string path) =>
        !path.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase);
}
