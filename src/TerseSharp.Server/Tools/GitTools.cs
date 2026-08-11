using System.Collections.Immutable;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class GitTools(ToolContext context)
{
    private const int MaxDiffLines = 400;

    [McpServerTool(Name = "changed_files", ReadOnly = true)]
    [Description("Replaces Bash git status and git diff --stat. One line per changed file - path, added and deleted line counts, and the status letter - so the end-of-task review costs a listing instead of a diff. Empty baseRef compares the working tree against HEAD and includes untracked files, path= scopes the listing to one path or pathspec the way diff_symbols and diff_text do, and exclude= drops the paths a path= cannot leave out - another session's notes on a shared tree, a scratch folder, an agent worktree. root= answers about any absolute directory instead of the loaded workspace - a sibling worktree or another repository, tagged outside-workspace - so no second load_workspace is needed.")]
    public Task<string> ChangedFiles(
        [Description("Commit, branch or range to compare against, e.g. main or HEAD~3. Empty compares the working tree against HEAD.")] string? baseRef = null,
        [Description("Limit to one path or pathspec, e.g. src or src/**/*.cs.")] string? path = null,
        [Description("Max results (200).")] int maxResults = 0,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Glob of paths to drop after path= has selected them, e.g. .research/** or **/*.md. Dropped files are not counted.")] string? exclude = null,
        [Description("Absolute directory to answer about instead of the loaded workspace, e.g. a sibling worktree. The answer is tagged outside-workspace.")] string? root = null,
        CancellationToken cancellationToken = default) =>
        root is { Length: > 0 }
            ? OutsideAsync(root, full => ListAsync(full, baseRef, path, exclude, NavigationTools.Cap(maxResults, 200), full, cancellationToken))
            : context.WithWorkspaceAsync(
                workspace,
                path,
                loaded => ListAsync(loaded.Root, baseRef, path, exclude, NavigationTools.Cap(maxResults, 200), null, cancellationToken),
                semantic: false,
                cancellationToken);

    [McpServerTool(Name = "diff_symbols", ReadOnly = true)]
    [Description("Replaces Bash git diff. Maps every changed hunk onto the declaration that contains it and answers with symbol ids you can feed straight to get_symbol_source - EXACT when a hunk sits inside one declaration, HEURISTIC with the raw line range when it does not. Use this to decide what to review, then read only the bodies you need. Unlike changed_files and diff_text it takes no root=: mapping a hunk to a declaration needs the Roslyn compilation, which only a loaded workspace has.")]
    public Task<string> DiffSymbols(
        [Description("Commit, branch or range to compare against. Empty compares the working tree against HEAD.")] string? baseRef = null,
        [Description("Limit to one path or pathspec.")] string? path = null,
        [Description("Max results (200).")] int maxResults = 0,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Not supported here: a hunk is mapped to a declaration through the Roslyn compilation, which needs the directory loaded. Pass it to changed_files or diff_text, or load_workspace that directory first.")] string? root = null,
        CancellationToken cancellationToken = default) =>
        root is { Length: > 0 }
            ? Task.FromResult(Errors.Invalid(
                "diff_symbols cannot answer about a directory that is not loaded - mapping a hunk onto a declaration needs its Roslyn compilation",
                "use changed_files root= or diff_text root= for an unloaded directory, or load_workspace it and drop root=").Render())
            : context.WithWorkspaceAsync(
                workspace,
                path,
                loaded => SymbolsAsync(loaded, baseRef, path, NavigationTools.Cap(maxResults, 200), cancellationToken),
                cancellationToken: cancellationToken);

    [McpServerTool(Name = "diff_text", ReadOnly = true)]
    [Description("Replaces Bash git diff. The raw unified diff, workspace-relative, for the hunk text a symbol read cannot show: whitespace, a non-.cs file, a pure deletion, and every hunk diff_symbols could only map HEURISTIC. Pass paths to diff up to 10 files in ONE call. Replaces one call per file: every entry is handed to the same git invocation as its own pathspec, so the answer is one unified diff already labelled per file. It costs about one line of response per changed line, so bound it: path= and paths= scope it and maxLines= caps it at 400 by default. root= answers about any absolute directory instead of the loaded workspace - a sibling worktree or another repository, tagged outside-workspace. diff_symbols first when the question is which declarations changed - it answers that in one line each.")]
    public Task<string> DiffText(
    [Description("Commit, branch or range to compare against. Empty compares the working tree against HEAD.")] string? baseRef = null,
    [Description("Limit to one path or pathspec; the cheapest way to bound the response.")] string? path = null,
    [Description("Several paths or pathspecs answered in one diff, at most 10. Replaces one call per file. Combines with path, which is taken first; a blank entry and an 11th entry are refused by name rather than dropped.")] string?[]? paths = null,
    [Description("Max diff lines returned (400).")] int maxLines = 0,
    [Description("Workspace or worktree name.")] string? workspace = null,
    [Description("Absolute directory to answer about instead of the loaded workspace, e.g. a sibling worktree. The answer is tagged outside-workspace.")] string? root = null,
    CancellationToken cancellationToken = default)
    {
        var combined = paths is { Length: > 0 } || path is { Length: > 0 }
            ? PluralPaths.Combine(path, paths, "paths")
            : Result.Ok(ImmutableArray<string>.Empty);

        if (!combined.IsOk)
            return Task.FromResult(combined.Error!.Render());

        var scoped = combined.Value;
        var hint = path ?? (scoped.IsDefaultOrEmpty ? null : scoped[0]);

        return root is { Length: > 0 }
            ? OutsideAsync(root, full => TextAsync(full, baseRef, scoped, NavigationTools.Cap(maxLines, MaxDiffLines), full, cancellationToken))
            : context.WithWorkspaceAsync(
                workspace,
                hint,
                loaded => TextAsync(loaded.Root, baseRef, scoped, NavigationTools.Cap(maxLines, MaxDiffLines), null, cancellationToken),
                semantic: false,
                cancellationToken);
    }

    private static async Task<string> ListAsync(
        string root,
        string? baseRef,
        string? path,
        string? exclude,
        int maxResults,
        string? outside,
        CancellationToken cancellationToken)
    {
        var numstat = await GitRunner.ReadAsync(root, Arguments(["diff", "--numstat"], baseRef, path), cancellationToken).ConfigureAwait(false);

        if (!numstat.IsOk)
            return numstat.Error!.Render();

        var status = await GitRunner.ReadAsync(root, Arguments(["diff", "--name-status"], baseRef, path), cancellationToken).ConfigureAwait(false);

        if (!status.IsOk)
            return status.Error!.Render();

        var untracked = baseRef is { Length: > 0 }
            ? Result.Ok(string.Empty)
            : await GitRunner.ReadAsync(
                root,
                ["--no-optional-locks", "ls-files", "--others", "--exclude-standard", "--", path is { Length: > 0 } pathspec ? pathspec : "."],
                cancellationToken).ConfigureAwait(false);

        return untracked.IsOk
            ? Render(numstat.Value!, status.Value!, untracked.Value!, exclude, maxResults, outside)
            : untracked.Error!.Render();
    }

    private static string Render(string numstat, string nameStatus, string untracked, string? exclude, int maxResults, string? outside)
    {
        var lines = Lines(numstat, nameStatus, untracked, Excluded(exclude));
        var response = new ResponseBuilder("changed_files", string.Empty);

        response.Summary(ResultCap.Shown(lines.Count, maxResults), lines.Count, "files", "path=, exclude=, baseRef= or maxResults=");

        if (outside is { Length: > 0 })
            response.Note("outside-workspace  " + outside);

        foreach (var line in lines.Capped(maxResults))
            response.Line(line);

        return response.ToString();
    }

    private static string Counted(int value) => value < 0 ? "?" : value.ToString(CultureInfo.InvariantCulture);

    private static async Task<string> SymbolsAsync(
        LoadedWorkspace workspace,
        string? baseRef,
        string? path,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var diff = await GitRunner.ReadAsync(
            workspace.Root,
            Arguments(["diff", "--unified=0", "--no-color"], baseRef, path),
            cancellationToken).ConfigureAwait(false);

        return diff.IsOk
            ? await DiffSymbolService.MapAsync(workspace, diff.Value!, maxResults, cancellationToken).ConfigureAwait(false)
            : diff.Error!.Render();
    }

    private static async Task<string> TextAsync(
    string root,
    string? baseRef,
    IReadOnlyList<string> paths,
    int maxLines,
    string? outside,
    CancellationToken cancellationToken)
    {
        var diff = await GitRunner.ReadAsync(
            root,
            Arguments(["diff", "--no-color"], baseRef, paths),
            cancellationToken).ConfigureAwait(false);

        if (!diff.IsOk)
            return diff.Error!.Render();

        var lines = new List<string>(maxLines);
        var total = 0;

        foreach (var line in diff.Value!.AsSpan().EnumerateLines())
        {
            total++;

            if (lines.Count < maxLines)
                lines.Add(new string(line));
        }

        var response = new ResponseBuilder("diff_text", paths.Count is 0 ? string.Empty : string.Join(" ", paths));

        response.Summary(lines.Count, total, "lines", "path=, paths= or maxLines=");

        if (outside is { Length: > 0 })
            response.Note("outside-workspace  " + outside);

        foreach (var line in lines)
            response.Line(line);

        return response.ToString();
    }

    private static string[] Arguments(IReadOnlyList<string> command, string? baseRef, string? path) =>
    Arguments(command, baseRef, path is { Length: > 0 } pathspec ? [pathspec] : []);

    private static string[] Arguments(IReadOnlyList<string> command, string? baseRef, IReadOnlyList<string> paths)
    {
        var arguments = new List<string>(command.Count + 7 + paths.Count) { "--no-optional-locks" };

        arguments.AddRange(command);
        arguments.AddRange(["--no-renames", "--no-ext-diff", "--relative"]);
        arguments.Add(baseRef is { Length: > 0 } reference ? reference : "HEAD");
        arguments.Add("--");

        if (paths.Count is 0)
            arguments.Add(".");
        else
            arguments.AddRange(paths);

        return [.. arguments];
    }

    private static FileGlob? Excluded(string? exclude) =>
        exclude is { Length: > 0 } glob ? FileGlob.Compile(glob) : null;


    private static bool Dropped(FileGlob? exclude, ReadOnlySpan<char> path) =>
        exclude is { } matcher && matcher.MatchesRelative(path);


    private static string Described(ChangedFile file, IReadOnlyDictionary<string, string> statuses) => string.Create(
        CultureInfo.InvariantCulture,
        $"{file.Path}  +{Counted(file.Added)} -{Counted(file.Deleted)}  {statuses.GetValueOrDefault(file.Path, "M")}");

    private static List<string> Lines(string numstat, string nameStatus, string untracked, FileGlob? exclude)
    {
        var statuses = DiffParser.NameStatus(nameStatus);
        var files = DiffParser.NumStat(numstat);
        var lines = new List<string>(files.Count + 8);

        foreach (var file in files)
        {
            if (!Dropped(exclude, file.Path))
                lines.Add(Described(file, statuses));
        }

        foreach (var line in untracked.AsSpan().EnumerateLines())
        {
            var path = line.Trim();

            if (!path.IsWhiteSpace() && !Dropped(exclude, path))
                lines.Add(new string(path) + "  +? -?  ?");
        }

        return lines;
    }

    private static Result<string> Outside(string root)
    {
        if (!Path.IsPathFullyQualified(root))
        {
            return Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"root '{root}' is not an absolute path"),
                "pass an absolute directory, or drop root= to answer about the loaded workspace"));
        }

        var full = Path.GetFullPath(root);

        return Directory.Exists(full) ? Result.Ok(full) : Result.Fail<string>(Errors.DocumentNotFound(root));
    }

    private static Task<string> OutsideAsync(string root, Func<string, Task<string>> action)
    {
        var resolved = Outside(root);

        return resolved.IsOk ? action(resolved.Value!) : Task.FromResult(resolved.Error!.Render());
    }
}
