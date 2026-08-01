namespace TerseSharp.Core;

public static class CleanService
{
    private const int MaxListedDirectories = 50;

    private static readonly string[] ProjectExtensions = [".csproj", ".vbproj", ".fsproj"];

    private static readonly string[] OutputNames = ["bin", "obj"];

    private static readonly EnumerationOptions Recursive =
        new() { RecurseSubdirectories = true, IgnoreInaccessible = true };

    public static Result<CleanRun> Clean(
        WorkspaceTarget target,
        string? project,
        bool includeIntermediate,
        bool dryRun,
        bool verbose,
        CancellationToken cancellationToken)
    {
        var roots = Roots(target, project, cancellationToken);

        if (!roots.IsOk)
            return Result.Fail<CleanRun>(roots.Error!);

        var removals = Outputs(target.Root, roots.Value!, includeIntermediate)
            .Select(directory => Remove(directory, dryRun, cancellationToken))
            .ToArray();

        return Result.Ok(new CleanRun(
            Render(target.Root, project, roots.Value!.Length, removals, dryRun, verbose),
            removals.Any(removal => removal.Locked)));
    }

    private static Result<string[]> Roots(WorkspaceTarget target, string? project, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(project))
            return Result.Ok(ProjectDirectories(target.Root, cancellationToken));

        var resolved = PathGuard.Resolve(target.Root, project);

        if (!resolved.IsOk)
            return Result.Fail<string[]>(resolved.Error!);

        var full = resolved.Value!;

        if (Directory.Exists(full))
            return Result.Ok<string[]>([full]);

        return File.Exists(full) && Path.GetDirectoryName(full) is { Length: > 0 } owner
            ? Result.Ok<string[]>([owner])
            : Result.Fail<string[]>(Errors.DocumentNotFound(project));
    }

    private static string[] ProjectDirectories(string root, CancellationToken cancellationToken)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(root, "*.*proj", Recursive))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsProject(file) && Path.GetDirectoryName(file) is { Length: > 0 } directory)
                directories.Add(directory);
        }

        return [.. directories.Order(StringComparer.Ordinal)];
    }

    private static bool IsProject(string file) =>
        ProjectExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    private static string[] Outputs(string root, string[] roots, bool includeIntermediate) =>
        [.. roots
            .SelectMany(directory => Candidates(directory, includeIntermediate))
            .Where(candidate => Directory.Exists(candidate) && PathBoundary.Contains(root, candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)];

    private static IEnumerable<string> Candidates(string directory, bool includeIntermediate) =>
        OutputNames.Take(includeIntermediate ? 2 : 1).Select(name => Path.Combine(directory, name));

    private static Removal Remove(string directory, bool dryRun, CancellationToken cancellationToken)
    {
        var measured = Measure(directory, cancellationToken);

        if (dryRun)
            return measured;

        try
        {
            Directory.Delete(directory, recursive: true);

            return measured;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return measured with { Locked = true };
        }
    }

    private static Removal Measure(string directory, CancellationToken cancellationToken)
    {
        var files = 0;
        var bytes = 0L;

        foreach (var file in Directory.EnumerateFiles(directory, "*", Recursive))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files++;
            bytes += Length(file);
        }

        return new Removal(directory, files, bytes, false);
    }

    private static long Length(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string Render(string root, string? project, int projects, Removal[] removals, bool dryRun, bool verbose)
    {
        var locked = removals.Count(removal => removal.Locked);
        var response = new ResponseBuilder("clean", project ?? PositionFormat.Relative(root, root));

        response.Summary(Math.Min(removals.Length, MaxListedDirectories), removals.Length, "directories", "project=");
        response.Note(Counters(projects, removals, locked, dryRun));

        if (locked is 0 && !verbose && !dryRun)
            return response.Note("(verbose=true for the per-directory list)").ToString();

        if (locked > 0)
        {
            response.Note("WARNING a locked file blocked the delete; the counters above exclude every LOCKED directory");
            response.Note("remedy: stop whatever holds the output, or unload_workspace and retry");
        }

        foreach (var removal in removals.Take(MaxListedDirectories))
            response.Line(Describe(root, removal));

        return response.ToString();
    }

    private static string Counters(int projects, Removal[] removals, int locked, bool dryRun)
    {
        var freed = removals.Where(removal => !removal.Locked).ToArray();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"projects={projects} files={freed.Sum(removal => removal.Files)} freedBytes={freed.Sum(removal => removal.Bytes)} locked={locked} state={State(dryRun, locked, removals.Length)}");
    }

    private static string State(bool dryRun, int locked, int total) => (dryRun, locked) switch
    {
        (true, _) => "dryRun",
        (false, 0) => "deleted",
        _ when locked == total => "blocked",
        _ => "partial",
    };

    private static string Describe(string root, Removal removal) => string.Create(
        CultureInfo.InvariantCulture,
        $"{(removal.Locked ? "LOCKED" : "REMOVED")} {PositionFormat.Relative(root, removal.Directory)} files={removal.Files} bytes={removal.Bytes}");

    private readonly record struct Removal(string Directory, int Files, long Bytes, bool Locked);
}

public readonly record struct CleanRun(string Response, bool Locked);
