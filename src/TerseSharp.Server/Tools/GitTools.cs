using System.Collections.Immutable;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class GitTools(ToolContext context)
{
    private const int MaxDiffLines = 1000;

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
        FileGlob.Unsupported(exclude, "exclude") is { } rejected
            ? Task.FromResult(rejected.Render())
            : root is { Length: > 0 }
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
    [Description("Max diff lines returned (1000). A truncated answer names the exact maxLines= that returns the rest, so one retry is enough.")] int maxLines = 0,
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
        var listed = Lines(numstat, nameStatus, untracked, Excluded(exclude));
        var response = new ResponseBuilder("changed_files", string.Empty);
        var capped = ResultCap.Shown(listed.Rows.Count, maxResults);
        var shown = listed.Rows.Capped(maxResults).ToArray();

        response.Summary(
            capped < listed.Rows.Count ? Covered(shown) : listed.Files,
            listed.Files,
            "files",
            "path=, exclude=, baseRef= or maxResults=");

        if (outside is { Length: > 0 })
            response.Note("outside-workspace  " + outside);

        foreach (var line in shown)
            response.Line(line);

        if (outside is not { Length: > 0 } && ArgumentLine.Paths(shown.Where(row => !Folded(row))) is { } batch)
            response.Note(batch);

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

        response.Summary(
            lines.Count,
            total,
            "lines",
            string.Create(CultureInfo.InvariantCulture, $"path=, paths= or maxLines={total}"));

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

    private static Listed Lines(string numstat, string nameStatus, string untracked, FileGlob? exclude)
    {
        var statuses = DiffParser.NameStatus(nameStatus);
        var files = DiffParser.NumStat(numstat);
        var rows = new List<string>(files.Count + 8);

        foreach (var file in files)
        {
            if (!Dropped(exclude, file.Path))
                rows.Add(Described(file, statuses));
        }

        var tracked = rows.Count;
        var kept = Kept(untracked, exclude);

        Untracked(kept, rows);

        return new Listed(rows, tracked + kept.Count);
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

    [McpServerTool(Name = "history", ReadOnly = true)]
    [Description("Replaces Bash git log and git show --stat. Commits touching a path, one line each - short sha, author date, author, subject - workspace-relative and oneline by default. baseRef takes a commit, a branch or a range such as v0.32.0..HEAD; contains= is git's pickaxe, listing only the commits whose diff added or removed that literal; message= greps subject and body. commit= answers one commit instead - its subject and one line per file with added and deleted counts - and is refused beside baseRef=, contains= or message= rather than ignoring them. root= answers about any absolute directory, tagged outside-workspace.")]
    public Task<string> History(
            [Description("Commit, branch or range to list, e.g. main, HEAD~20 or v0.32.0..HEAD. Empty lists from HEAD backwards.")] string? baseRef = null,
            [Description("Limit to one path or pathspec, e.g. src or src/**/*.cs.")] string? path = null,
            [Description("Only commits whose diff added or removed this literal - git's pickaxe, which no text search over the working tree can answer.")] string? contains = null,
            [Description("Only commits whose subject or body matches this text.")] string? message = null,
            [Description("One commit instead of a listing: its subject and one line per file with added and deleted counts. Cannot be combined with baseRef=, contains= or message=.")] string? commit = null,
            [Description("Max commits (50).")] int maxResults = 0,
            [Description("Workspace or worktree name.")] string? workspace = null,
            [Description("Absolute directory to answer about instead of the loaded workspace. The answer is tagged outside-workspace.")] string? root = null,
            CancellationToken cancellationToken = default)
    {
        if (Conflicting(commit, baseRef, contains, message) is { } refusal)
            return Task.FromResult(refusal.Render());

        return root is { Length: > 0 }
            ? OutsideAsync(root, full => HistoryAsync(full, baseRef, path, contains, message, commit, NavigationTools.Cap(maxResults, 50), full, cancellationToken))
            : context.WithWorkspaceAsync(
                workspace,
                path,
                loaded => HistoryAsync(loaded.Root, baseRef, path, contains, message, commit, NavigationTools.Cap(maxResults, 50), null, cancellationToken),
                semantic: false,
                cancellationToken);
    }

    private static async Task<string> HistoryAsync(
            string root,
            string? baseRef,
            string? path,
            string? contains,
            string? message,
            string? commit,
            int maxResults,
            string? outside,
            CancellationToken cancellationToken)
    {
        var run = await GitRunner.ReadAsync(root, HistoryArguments(baseRef, path, contains, message, commit, maxResults), cancellationToken).ConfigureAwait(false);

        if (!run.IsOk)
            return run.Error!.Render();

        var lines = new List<string>();

        foreach (var line in run.Value!.AsSpan().EnumerateLines())
        {
            if (!line.IsWhiteSpace())
                lines.Add(new string(line.Trim()));
        }

        var response = new ResponseBuilder("history", commit ?? path ?? string.Empty);
        var shown = Math.Min(lines.Count, maxResults);

        response.Summary(shown, shown, commit is { Length: > 0 } ? "lines" : "commits");

        if (outside is { Length: > 0 })
            response.Note("outside-workspace  " + outside);

        if (lines.Count > maxResults)
            response.Note("more commits match than were listed - raise maxResults=, or narrow with path=, contains=, message= or baseRef=");

        foreach (var line in lines.Capped(maxResults))
            response.Line(line);

        return response.ToString();
    }

    private static string[] HistoryArguments(
            string? baseRef,
            string? path,
            string? contains,
            string? message,
            string? commit,
            int maxResults)
    {
        if (commit is { Length: > 0 } one)
            return Arguments(["show", "--stat=200", "--oneline", "--no-color", "--relative", one], null, path);

        var command = new List<string>(10)
            {
                "log",
                "--no-color",
                "--relative",
                "--date=short",
                "--pretty=format:%h %ad %an %s",
                "--max-count=" + (maxResults + 1).ToString(CultureInfo.InvariantCulture),
            };

        if (contains is { Length: > 0 } literal)
            command.Add("-S" + literal);

        if (message is { Length: > 0 } subject)
            command.Add("--grep=" + subject);

        return Arguments(command, baseRef, path);
    }

    private static TerseError? Conflicting(string? commit, string? baseRef, string? contains, string? message)
    {
        if (commit is not { Length: > 0 })
            return null;

        var ignored = new List<string>(3);

        if (baseRef is { Length: > 0 })
            ignored.Add("baseRef=");

        if (contains is { Length: > 0 })
            ignored.Add("contains=");

        if (message is { Length: > 0 })
            ignored.Add("message=");

        return ignored.Count is 0
            ? null
            : Errors.Invalid(
                "commit= answers one commit, so it cannot be combined with " + string.Join(", ", ignored),
                "drop commit= to list commits with those filters, or drop the filters to describe that one commit");
    }

    private const int UntrackedFold = 5;

    private readonly record struct Listed(List<string> Rows, int Files);

    private static List<string> Kept(string untracked, FileGlob? exclude)
    {
        var kept = new List<string>();

        foreach (var line in untracked.AsSpan().EnumerateLines())
        {
            var path = line.Trim();

            if (!path.IsWhiteSpace() && !Dropped(exclude, path))
                kept.Add(new string(path));
        }

        return kept;
    }

    private static void Untracked(List<string> kept, List<string> rows)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var byDirectory = counts.GetAlternateLookup<ReadOnlySpan<char>>();

        foreach (var path in kept)
        {
            var directory = TopDirectory(path);

            if (!directory.IsEmpty && !byDirectory.TryAdd(directory, 1))
                byDirectory[directory] += 1;
        }

        var folded = new HashSet<string>(StringComparer.Ordinal);
        var seen = folded.GetAlternateLookup<ReadOnlySpan<char>>();

        foreach (var path in kept)
        {
            var directory = TopDirectory(path);

            if (directory.IsEmpty || byDirectory[directory] <= UntrackedFold)
                rows.Add(path + "  +? -?  ?");
            else if (seen.Add(directory))
                rows.Add(string.Create(CultureInfo.InvariantCulture, $"{directory}/**  +? -?  ?  x{byDirectory[directory]} untracked"));
        }
    }

    private static ReadOnlySpan<char> TopDirectory(ReadOnlySpan<char> path)
    {
        var separator = path.IndexOfAny('/', '\\');

        return separator > 0 ? path[..separator] : default;
    }

    private static bool Folded(string row)
    {
        var end = row.IndexOf("  ", StringComparison.Ordinal);

        return (end < 0 ? row.AsSpan() : row.AsSpan(0, end)).EndsWith("/**", StringComparison.Ordinal);
    }

    private static int Covered(IReadOnlyList<string> rows)
    {
        var files = 0;

        foreach (var row in rows)
            files += Files(row);

        return files;
    }

    private static int Files(string row)
    {
        const string Untracked = " untracked";

        if (!Folded(row) || !row.EndsWith(Untracked, StringComparison.Ordinal))
            return 1;

        var counted = row.AsSpan(0, row.Length - Untracked.Length);
        var marker = counted.LastIndexOf("  x", StringComparison.Ordinal);

        return marker >= 0 && int.TryParse(counted[(marker + 3)..], CultureInfo.InvariantCulture, out var folded)
            ? folded
            : 1;
    }
}
