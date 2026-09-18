using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class GitTools(ToolContext context, ListingMemo listings)
{
    private const int MaxDiffLines = 3000;

    [McpServerTool(Name = "changed_files", ReadOnly = true)]
    [Description("Replaces Bash git status and git diff --stat and git diff --cached --name-only. One line per changed file - path, added and deleted line counts, and the status letter - so the end-of-task review costs a listing instead of a diff. Empty baseRef compares the working tree against HEAD and includes untracked files; staged=true answers the INDEX instead and untracked=false drops the files git does not track. path= scopes the listing to one path or pathspec the way diff_symbols and diff_text do, and exclude= drops the paths a path= cannot leave out - another session's notes on a shared tree, a scratch folder, an agent worktree. A listing carrying both kinds says how many of each it counted, and one carrying tracked changes ends with the diff_symbols call that maps them onto declarations. root= answers about any absolute directory instead of the loaded workspace - a sibling worktree or another repository, tagged outside-workspace - so no second load_workspace is needed. A byte-identical repeat with no watcher event and no git state change since replays the previous listing plus an UNCHANGED marker instead of re-shelling to git.")]
    public Task<string> ChangedFiles(
        [Description("Commit, branch or range to compare against, e.g. main or HEAD~3. Empty compares the working tree against HEAD.")] string? baseRef = null,
        [Description("Limit to one path or pathspec, e.g. src or src/**/*.cs.")] string? path = null,
        [Description("Max results (200).")] int maxResults = 0,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Glob of paths to drop after path= has selected them, e.g. .research/** or **/*.md. Dropped files are not counted.")] string? exclude = null,
        [Description("Absolute directory to answer about instead of the loaded workspace, e.g. a sibling worktree. The answer is tagged outside-workspace.")] string? root = null,
        [Description("List what is STAGED - the index against HEAD, or against baseRef. Untracked files are never listed. Default false.")] bool staged = false,
        [Description("Include files git does not track. Default true; false answers tracked changes only, which is what git status --untracked-files=no asks.")] bool untracked = true,
        CancellationToken cancellationToken = default) =>
        root is { Length: > 0 }
            ? OutsideAsync(root, full => ListAsync(full, baseRef, path, exclude, NavigationTools.Cap(maxResults, 200), full, new ChangeScope(staged, untracked), maxResults > 0, cancellationToken))
            : context.WithWorkspaceAsync(
                workspace,
                path,
                loaded => baseRef is { Length: > 0 }
                ? ListAsync(loaded.Root, baseRef, path, exclude, NavigationTools.Cap(maxResults, 200), null, new ChangeScope(staged, untracked), maxResults > 0, cancellationToken)
                : ListMemoizedAsync(loaded, path, exclude, NavigationTools.Cap(maxResults, 200), new ChangeScope(staged, untracked), maxResults > 0, cancellationToken),
                semantic: false,
                cancellationToken);

    [McpServerTool(Name = "diff_symbols", ReadOnly = true)]
    [Description("Replaces Bash git diff. Maps every changed hunk onto the declaration that contains it and answers with symbol ids you can feed straight to get_symbol_source - EXACT when a hunk sits inside one declaration, HEURISTIC with the raw line range when it does not. Use this to decide what to review, then read only the bodies you need. Unlike changed_files and diff_text it takes no root=: mapping a hunk to a declaration needs the Roslyn compilation, which only a loaded workspace has.")]
    public Task<string> DiffSymbols(
        [Description("Commit, branch or range to compare against. Empty compares the working tree against the INDEX, so a fully staged change set maps nothing - pass staged=true for the index against HEAD.")] string? baseRef = null,
        [Description("Map the INDEX instead of the working tree - git diff --cached. Default false.")] bool staged = false,
        [Description("Limit to one path or pathspec.")] string? path = null,
        [Description("Max results (200).")] int maxResults = 0,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Not supported here: mapping a hunk to a declaration needs the directory loaded. Pass it to changed_files or diff_text.")] string? root = null,
        CancellationToken cancellationToken = default) =>
        root is { Length: > 0 }
            ? Task.FromResult(Errors.Invalid(
                "diff_symbols cannot answer about a directory that is not loaded - mapping a hunk onto a declaration needs its Roslyn compilation",
                "use changed_files root= or diff_text root= for an unloaded directory, or load_workspace it and drop root=").Render())
            : context.WithWorkspaceAsync(
                workspace,
                path,
                loaded => SymbolsAsync(loaded, baseRef, path, NavigationTools.Cap(maxResults, 200), staged, cancellationToken),
                cancellationToken: cancellationToken);

    [McpServerTool(Name = "diff_text", ReadOnly = true)]
    [Description("Replaces Bash git diff. The raw unified diff, workspace-relative, for the hunk text a symbol read cannot show: whitespace, a non-.cs file, a pure deletion, and every hunk diff_symbols could only map HEURISTIC. Pass paths to diff up to 10 files in ONE call. Replaces one call per file: every entry is handed to the same git invocation as its own pathspec, so the answer is one unified diff already labelled per file. It costs about one line of response per changed line, so bound it: path= and paths= scope it and maxLines= caps it at 3000 by default. A clipped answer is labelled INCOMPLETE and names the skipLines= that returns the rest without re-paying the lines you already have. root= answers about any absolute directory instead of the loaded workspace - a sibling worktree or another repository, tagged outside-workspace. diff_symbols first when the question is which declarations changed - it answers that in one line each.")]
    public Task<string> DiffText(
    [Description("Commit, branch or range to compare against. Empty compares the working tree against the INDEX, so a fully staged change set reads 0 lines - pass staged=true for the index against HEAD.")] string? baseRef = null,
    [Description("Answer the INDEX instead of the working tree - git diff --cached. Default false.")] bool staged = false,
    [Description("Limit to one path or pathspec; the cheapest way to bound the response.")] string? path = null,
    [Description("Several paths or pathspecs answered in one diff, at most 10. Replaces one call per file. Combines with path, which is taken first; a blank entry and an 11th entry are refused by name rather than dropped.")] string?[]? paths = null,
    [Description("Max diff lines returned (3000). A clipped answer is labelled INCOMPLETE and names the skipLines= that continues it.")] int maxLines = 0,
    [Description("Workspace or worktree name.")] string? workspace = null,
    [Description("Absolute directory to answer about instead of the loaded workspace, e.g. a sibling worktree. The answer is tagged outside-workspace.")] string? root = null,
    [Description("Skip the first N diff lines - the continuation an INCOMPLETE answer names, so the rest never re-pays the prefix. Default 0.")] int skipLines = 0,
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
            ? OutsideAsync(root, full => TextAsync(full, baseRef, scoped, NavigationTools.Cap(maxLines, MaxDiffLines), full, staged, skipLines, cancellationToken))
            : context.WithWorkspaceAsync(
                workspace,
                hint,
                loaded => TextAsync(loaded.Root, baseRef, scoped, NavigationTools.Cap(maxLines, MaxDiffLines), null, staged, skipLines, cancellationToken),
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
        ChangeScope scope,
        bool chosen,
        CancellationToken cancellationToken)
    {
        string[] command = scope.Staged ? ["diff", "--cached"] : ["diff"];
        var numstat = await GitRunner.ReadAsync(root, Arguments([.. command, "--numstat"], baseRef, path), cancellationToken).ConfigureAwait(false);

        if (!numstat.IsOk)
            return numstat.Error!.Render();

        var status = await GitRunner.ReadAsync(root, Arguments([.. command, "--name-status"], baseRef, path), cancellationToken).ConfigureAwait(false);

        if (!status.IsOk)
            return status.Error!.Render();

        var untracked = baseRef is { Length: > 0 } || scope.Staged || !scope.Untracked
            ? Result.Ok(string.Empty)
            : await GitRunner.ReadAsync(
                root,
                ["--no-optional-locks", "ls-files", "--others", "--exclude-standard", "--", path is { Length: > 0 } pathspec ? pathspec : "."],
                cancellationToken).ConfigureAwait(false);

        return untracked.IsOk
            ? Render(numstat.Value!, status.Value!, untracked.Value!, exclude, path, maxResults, outside, scope.Staged ? null : Steer(baseRef, path), chosen)
            : untracked.Error!.Render();
    }

    private static string Render(
            string numstat,
            string nameStatus,
            string untracked,
            string? exclude,
            string? path,
            int maxResults,
            string? outside,
            string? steer,
            bool chosen)
    {
        var listed = Lines(numstat, nameStatus, untracked, Excluded(exclude), path);
        var response = new ResponseBuilder("changed_files", string.Empty).Chosen(chosen);
        var capped = chosen ? ResultCap.Shown(listed.Rows.Count, maxResults) : ResultCap.Whole(listed.Rows.Count, maxResults);
        var shown = (chosen ? listed.Rows.Capped(maxResults) : listed.Rows.CappedWhole(maxResults)).ToArray();

        response.Summary(
            capped < listed.Rows.Count ? Covered(shown) : listed.Files,
            listed.Files,
            "files",
            "path=, exclude=, baseRef= or maxResults=");

        if (listed.Tracked > 0 && listed.Files > listed.Tracked)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"tracked={listed.Tracked} untracked={listed.Files - listed.Tracked} - untracked=false or exclude= drops what path= cannot"));

        if (Array.Exists(shown, Folded))
            response.Note("a /** row folds the untracked files of ONE directory - pass path=<that directory> to list what it covers, one level at a time");

        if (outside is { Length: > 0 })
            response.Note("outside-workspace  " + outside);

        foreach (var line in shown)
            response.Line(line);

        if (outside is not { Length: > 0 } && listed.Tracked > 0 && steer is { Length: > 0 })
            response.Note(steer);

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
        bool staged,
        CancellationToken cancellationToken)
    {
        string[] command = staged ? ["diff", "--cached", "--unified=0", "--no-color"] : ["diff", "--unified=0", "--no-color"];

        var diff = await GitRunner.ReadAsync(
            workspace.Root,
            Arguments(command, baseRef, path),
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
            bool staged,
            int skipLines,
            CancellationToken cancellationToken)
    {
        string[] command = staged ? ["diff", "--cached", "--no-color"] : ["diff", "--no-color"];

        var diff = await GitRunner.ReadAsync(
            root,
            Arguments(command, baseRef, paths),
            cancellationToken).ConfigureAwait(false);

        if (!diff.IsOk)
            return diff.Error!.Render();

        var skipped = Math.Max(0, skipLines);
        var lines = new List<string>(Math.Min(maxLines, 512));
        var total = 0;

        foreach (var line in diff.Value!.AsSpan().EnumerateLines())
        {
            total++;

            if (total > skipped && lines.Count < maxLines)
                lines.Add(new string(line));
        }

        var response = new ResponseBuilder("diff_text", paths.Count is 0 ? string.Empty : string.Join(" ", paths));

        response.Summary(
            lines.Count,
            skipped is 0 ? total : lines.Count,
            "lines",
            string.Create(CultureInfo.InvariantCulture, $"path=, paths=, or continue with skipLines={skipped + lines.Count}"));

        if (skipped > 0)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"window starts after skipLines={skipped} of {total} diff lines"));

        if (outside is { Length: > 0 })
            response.Note("outside-workspace  " + outside);

        foreach (var line in lines)
            response.Line(line);

        if (skipped + lines.Count < total)
        {
            response.Note(string.Create(
                CultureInfo.InvariantCulture,
                $"INCOMPLETE - {skipped + lines.Count} of {total} diff lines seen; next: skipLines={skipped + lines.Count} maxLines={maxLines} returns the rest without re-paying these lines"));
        }

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

    private static Listed Lines(string numstat, string nameStatus, string untracked, FileGlob? exclude, string? path)
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

        Untracked(kept, rows, path);

        return new Listed(rows, tracked + kept.Count, tracked);
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
    [Description("Replaces Bash git log and git show --stat and git tag --list and git describe and git ls-remote --tags. Commits touching a path, one line each - short sha, author date, author, subject - workspace-relative and oneline by default. baseRef takes a commit, a branch or a range such as v0.32.0..HEAD; contains= is git's pickaxe, listing only the commits whose diff added or removed that literal; message= greps subject and body. commit= answers one commit instead - its subject and one line per file with added and deleted counts - and is refused beside baseRef=, contains= or message= rather than ignoring them. tags=true answers the repository's tags instead, newest first, one line each with the commit it names, and remote=true merges origin's tags into it, tagging every row local=yes|no remote=yes|no. describe=true answers HEAD's own position instead - nearest tag, commits on top of it, short sha, dirty flag - which is the MinVer question a release asks. root= answers about any absolute directory, tagged outside-workspace.")]
    public Task<string> History(
            [Description("Commit, branch or range to list, e.g. main, HEAD~20 or v0.32.0..HEAD. Empty lists from HEAD backwards.")] string? baseRef = null,
            [Description("Limit to one path or pathspec, e.g. src or src/**/*.cs.")] string? path = null,
            [Description("Only commits whose diff added or removed this literal - git's pickaxe.")] string? contains = null,
            [Description("Only commits whose subject or body matches this text.")] string? message = null,
            [Description("One commit instead of a listing: its subject and one line per file with added and deleted counts. Cannot be combined with baseRef=, contains= or message=.")] string? commit = null,
            [Description("List tags instead of commits, newest version first - name, short sha, date. Refused beside baseRef=, path=, contains=, message= or commit=.")] bool tags = false,
            [Description("Answer HEAD's position instead of a listing: nearest tag, commits since it, short sha, dirty flag - one line. Refused beside every filter.")] bool describe = false,
            [Description("Beside tags=true, also read origin's tags and tag every row local=yes|no remote=yes|no. Rows only the remote has come FIRST, so the cap never drops them; they carry no date. Refused on its own.")] bool remote = false,
            [Description("Max commits, or tags (50).")] int maxResults = 0,
            [Description("Workspace or worktree name.")] string? workspace = null,
            [Description("Absolute directory to answer about instead of the loaded workspace. The answer is tagged outside-workspace.")] string? root = null,
            CancellationToken cancellationToken = default)
    {
        if (Conflicting(commit, baseRef, contains, message) is { } refusal)
            return Task.FromResult(refusal.Render());

        if (Published(remote, tags) is { } unmergeable)
            return Task.FromResult(unmergeable.Render());

        if (Unrelated(tags, commit, baseRef, contains, message, path) is { } tagged)
            return Task.FromResult(tagged.Render());

        if (Solo(describe, tags, commit, baseRef, contains, message, path) is { } positioned)
            return Task.FromResult(positioned.Render());

        return root is { Length: > 0 }
            ? OutsideAsync(root, full => HistoryAsync(full, baseRef, path, contains, message, commit, tags, describe, remote, NavigationTools.Cap(maxResults, 50), full, maxResults > 0, cancellationToken))
            : context.WithWorkspaceAsync(
                workspace,
                path,
                loaded => HistoryAsync(loaded.Root, baseRef, path, contains, message, commit, tags, describe, remote, NavigationTools.Cap(maxResults, 50), null, maxResults > 0, cancellationToken),
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
            bool tags,
            bool describe,
            bool remote,
            int maxResults,
            string? outside,
            bool chosen,
            CancellationToken cancellationToken)
    {
        if (remote)
            return await RemoteTagsAsync(root, maxResults, outside, chosen, cancellationToken).ConfigureAwait(false);

        var arguments = Requested(baseRef, path, contains, message, commit, tags, describe, maxResults);
        var run = await GitRunner.ReadAsync(root, arguments, cancellationToken).ConfigureAwait(false);

        if (!run.IsOk)
            return run.Error!.Render();

        return describe
            ? Describing(run.Value!, outside)
            : Rendered(run.Value!, Unit(tags, commit), commit ?? path ?? string.Empty, maxResults, outside, chosen);
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

    private readonly record struct Listed(List<string> Rows, int Files, int Tracked);

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

    private static void Untracked(List<string> kept, List<string> rows, string? path)
    {
        var raw = Scope(path);
        var buffer = raw.Length <= 256 ? stackalloc char[256] : new char[raw.Length];
        var scope = Normalized(raw, buffer);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var byDirectory = counts.GetAlternateLookup<ReadOnlySpan<char>>();

        Fold(kept, Tally(kept, scope, byDirectory), byDirectory, rows);
    }

    private static ReadOnlySpan<char> TopDirectory(ReadOnlySpan<char> path, ReadOnlySpan<char> scope)
    {
        var start = Below(path, scope);
        var separator = path[start..].IndexOfAny('/', '\\');

        return separator > 0 ? path[..(start + separator)] : default;
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

    private static string[] TagArguments(int maxResults) =>
    [
        "for-each-ref",
        "--sort=-v:refname",
        "--count=" + (maxResults + 1).ToString(CultureInfo.InvariantCulture),
        "--format=%(refname:short) %(if)%(*objectname)%(then)%(*objectname:short)%(else)%(objectname:short)%(end) %(if)%(*committerdate)%(then)%(*committerdate:short)%(else)%(creatordate:short)%(end)",
        "refs/tags",
    ];

    private static string Unit(bool tags, string? commit) => tags
        ? "tags"
        : commit is { Length: > 0 } ? "lines" : "commits";

    private static List<string> Trimmed(string output)
    {
        var lines = new List<string>();

        foreach (var line in output.AsSpan().EnumerateLines())
        {
            if (!line.IsWhiteSpace())
                lines.Add(new string(line.Trim()));
        }

        return lines;
    }

    private static string Rendered(string output, string unit, string target, int maxResults, string? outside, bool chosen = false)
    {
        var lines = Trimmed(output);
        var response = new ResponseBuilder("history", target).Chosen(chosen);
        var shown = Math.Min(lines.Count, maxResults);

        response.Summary(shown, shown, unit);

        if (outside is { Length: > 0 })
            response.Note("outside-workspace  " + outside);

        if (lines.Count > maxResults)
            response.Note(More(unit, chosen));

        foreach (var line in lines.Capped(maxResults))
            response.Line(line);

        return response.ToString();
    }

    private static string More(string unit, bool chosen) => (unit, chosen) switch
    {
        ("tags", true) => "more tags exist than were listed",
        ("tags", false) => "more tags exist than were listed - raise maxResults=",
        (_, true) => "more commits match than were listed - narrow with path=, contains=, message= or baseRef=",
        _ => "more commits match than were listed - raise maxResults=, or narrow with path=, contains=, message= or baseRef=",
    };

    private static TerseError? Unrelated(bool tags, string? commit, string? baseRef, string? contains, string? message, string? path)
    {
        if (!tags)
            return null;

        var ignored = new List<string>(5);

        Ignored(ignored, "commit=", commit);
        Ignored(ignored, "baseRef=", baseRef);
        Ignored(ignored, "contains=", contains);
        Ignored(ignored, "message=", message);
        Ignored(ignored, "path=", path);

        return ignored.Count is 0
            ? null
            : Errors.Invalid(
                "tags=true lists the repository's tags, so it cannot be combined with " + string.Join(", ", ignored),
                "drop tags=true to list commits with those filters, or drop the filters to list the tags");
    }

    private static void Ignored(List<string> ignored, string name, string? value)
    {
        if (value is { Length: > 0 })
            ignored.Add(name);
    }

    private readonly record struct ChangeScope(bool Staged, bool Untracked);

    private static string Steer(string? baseRef, string? path)
    {
        var arguments = new StringBuilder("next: diff_symbols");

        if (baseRef is { Length: > 0 } reference)
            arguments.Append(" baseRef=\"").Append(reference).Append('"');

        if (path is { Length: > 0 } pathspec)
            arguments.Append(" path=\"").Append(pathspec).Append('"');

        return arguments.Append(" - maps each hunk onto the declaration that contains it").ToString();
    }

    private static string[] Requested(
        string? baseRef,
        string? path,
        string? contains,
        string? message,
        string? commit,
        bool tags,
        bool describe,
        int maxResults)
    {
        if (describe)
            return ["describe", "--tags", "--long", "--dirty", "--always"];

        return tags ? TagArguments(maxResults) : HistoryArguments(baseRef, path, contains, message, commit, maxResults);
    }

    private static string Describing(string output, string? outside)
    {
        var response = new ResponseBuilder("history", "describe");

        if (outside is { Length: > 0 })
            response.Note("outside-workspace  " + outside);

        response.Line(Position(output));

        return response.ToString();
    }

    private static string Position(string output)
    {
        var text = output.AsSpan().Trim();
        var dirty = text.EndsWith("-dirty", StringComparison.Ordinal);
        var described = dirty ? text[..^"-dirty".Length] : text;
        var marker = described.LastIndexOf("-g", StringComparison.Ordinal);
        var state = dirty ? "true" : "false";

        if (marker < 0)
            return string.Create(CultureInfo.InvariantCulture, $"tag=NONE  sha={described}  dirty={state}  no tag is reachable from HEAD");

        var head = described[..marker];
        var ahead = head.LastIndexOf('-');

        return ahead < 0
            ? string.Create(CultureInfo.InvariantCulture, $"tag=NONE  sha={described[(marker + 2)..]}  dirty={state}  no tag is reachable from HEAD")
            : string.Create(CultureInfo.InvariantCulture, $"tag={head[..ahead]}  ahead={head[(ahead + 1)..]}  sha={described[(marker + 2)..]}  dirty={state}");
    }

    private static TerseError? Solo(bool describe, bool tags, string? commit, string? baseRef, string? contains, string? message, string? path)
    {
        if (!describe)
            return null;

        var ignored = new List<string>(6);

        Ignored(ignored, "tags=true", tags ? "true" : null);
        Ignored(ignored, "commit=", commit);
        Ignored(ignored, "baseRef=", baseRef);
        Ignored(ignored, "contains=", contains);
        Ignored(ignored, "message=", message);
        Ignored(ignored, "path=", path);

        return ignored.Count is 0
            ? null
            : Errors.Invalid(
                "describe=true answers HEAD's position against the nearest tag, so it cannot be combined with " + string.Join(", ", ignored),
                "drop describe=true to list commits or tags with those filters, or drop the filters to answer the position");
    }

    private async Task<string> ListMemoizedAsync(
        LoadedWorkspace loaded,
        string? path,
        string? exclude,
        int maxResults,
        ChangeScope scope,
        bool capped,
        CancellationToken cancellationToken)
    {
        var stamp = await GitStampAsync(loaded, cancellationToken).ConfigureAwait(false);
        var key = string.Join('\u0001', loaded.Root, path, exclude, maxResults.ToString(CultureInfo.InvariantCulture), scope.ToString(), capped.ToString());
        var now = Stopwatch.GetTimestamp();

        if (stamp is not null && listings.Replay(key, stamp, now) is { } replay)
            return replay;

        var text = await ListAsync(loaded.Root, null, path, exclude, maxResults, null, scope, capped, cancellationToken).ConfigureAwait(false);

        if (stamp is not null && !text.StartsWith("ERROR", StringComparison.Ordinal))
            listings.Remember(key, stamp, text, now);

        return text;
    }

    private static async Task<string?> GitStampAsync(LoadedWorkspace loaded, CancellationToken cancellationToken)
    {
        var sync = loaded.Sync;

        try
        {
            await sync.SyncAsync(loaded, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }

        if (sync.State is not WatchState.Active || sync.Gaps > 0 || sync.PendingCount > 0)
            return null;

        if (GitDirectory(loaded.Root) is not { } git)
            return null;

        var stamp = new StringBuilder(160);

        stamp.Append(sync.Generations.ToString())
            .Append('#')
            .Append(sync.Events.ToString(CultureInfo.InvariantCulture))
            .Append('#')
            .Append(sync.Stirred.ToString(CultureInfo.InvariantCulture));

        StatInto(stamp, Path.Combine(git, "index"));
        StatInto(stamp, Path.Combine(git, "HEAD"));
        StatInto(stamp, Path.Combine(git, "packed-refs"));

        if (await HeadTargetAsync(git, cancellationToken).ConfigureAwait(false) is { } head)
            StatInto(stamp, Path.Combine(git, head));

        return stamp.ToString();
    }

    private static string? GitDirectory(string root)
    {
        for (var current = root; current is { Length: > 0 };)
        {
            var candidate = Path.Combine(current, ".git");

            if (Directory.Exists(candidate))
                return candidate;

            if (File.Exists(candidate))
                return null;

            current = Path.GetDirectoryName(current) ?? string.Empty;
        }

        return null;
    }

    private static async Task<string?> HeadTargetAsync(string git, CancellationToken cancellationToken)
    {
        try
        {
            var head = await File.ReadAllTextAsync(Path.Combine(git, "HEAD"), cancellationToken).ConfigureAwait(false);

            return head.StartsWith("ref: ", StringComparison.Ordinal) ? head[5..].Trim() : null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void StatInto(StringBuilder stamp, string path)
    {
        var file = new FileInfo(path);

        if (file.Exists)
        {
            stamp.Append(' ')
                .Append(file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(file.Length.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            stamp.Append(" absent");
        }
    }

    private const string TagPrefix = "refs/tags/";
    private const int ShortSha = 7;

    private const string Peeled = "^{}";
    private static readonly string[] RemoteTagArguments = ["ls-remote", "--tags", "origin"];

    private static void Recorded(Dictionary<string, string> tags, string line)
    {
        var text = line.AsSpan();
        var tab = text.IndexOf('\t');

        if (tab <= 0)
            return;

        var reference = text[(tab + 1)..].Trim();

        if (!reference.StartsWith(TagPrefix, StringComparison.Ordinal))
            return;

        var name = reference[TagPrefix.Length..];

        tags[new string(name.EndsWith(Peeled, StringComparison.Ordinal) ? name[..^Peeled.Length] : name)] = new string(text[..Math.Min(tab, ShortSha)]);
    }

    private static Dictionary<string, string> RemoteTags(string output)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in Trimmed(output))
            Recorded(tags, line);

        return tags;
    }

    private static string TagName(string line)
    {
        var text = line.AsSpan();
        var space = text.IndexOf(' ');

        return new string(space < 0 ? text : text[..space]);
    }

    internal static string MergedTags(string local, string remote)
    {
        var published = RemoteTags(remote);
        var lines = new List<string>(published.Count + 8);
        var listed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in Trimmed(local))
            listed.Add(TagName(line));

        foreach (var pair in published)
        {
            if (!listed.Contains(pair.Key))
                lines.Add(pair.Key + " " + pair.Value + "  local=no remote=yes");
        }

        foreach (var line in Trimmed(local))
            lines.Add(line + (published.ContainsKey(TagName(line)) ? "  local=yes remote=yes" : "  local=yes remote=no"));

        return string.Join("\n", lines);
    }

    private static async Task<string> RemoteTagsAsync(string root, int maxResults, string? outside, bool chosen, CancellationToken cancellationToken)
    {
        var local = await GitRunner.ReadAsync(root, TagArguments(maxResults), cancellationToken).ConfigureAwait(false);

        if (!local.IsOk)
            return local.Error!.Render();

        var published = await GitRunner.ReadAsync(root, RemoteTagArguments, cancellationToken).ConfigureAwait(false);

        return published.IsOk
            ? Rendered(MergedTags(local.Value!, published.Value!), "tags", string.Empty, maxResults, outside, chosen)
            : published.Error!.Render();
    }

    private static TerseError? Published(bool remote, bool tags) => remote && !tags
        ? Errors.Invalid(
            "remote=true merges the remote's tag list into the local one, so it only answers beside tags=true",
            "pass tags=true remote=true to compare the two tag lists, or drop remote= to list commits")
        : null;

    private static int Below(ReadOnlySpan<char> path, ReadOnlySpan<char> scope)
    {
        if (scope.IsEmpty || path.Length <= scope.Length || !path.StartsWith(scope, StringComparison.OrdinalIgnoreCase))
            return 0;

        return path[scope.Length] is '/' or '\\' ? scope.Length + 1 : 0;
    }

    private static ReadOnlySpan<char> Scope(string? path)
    {
        if (path is not { Length: > 0 })
            return default;

        var span = Bare(path.AsSpan()).TrimEnd(Separators);
        var wildcard = span.IndexOfAny('*', '?');

        if (wildcard < 0)
            return span;

        var cut = span[..wildcard].LastIndexOfAny(Separators);

        return cut > 0 ? span[..cut] : default;
    }

    private static ReadOnlySpan<char> Separators => "/\\";

    private static ReadOnlySpan<char> Bare(ReadOnlySpan<char> path)
    {
        var span = path;

        if (span.StartsWith(":(", StringComparison.Ordinal) && span.IndexOf(')') is var close and >= 0)
            span = span[(close + 1)..];

        while (span.Length > 1 && span[0] is '.' && span[1] is '/' or '\\')
            span = span[2..];

        return span;
    }

    private static int[] Tally(
            List<string> kept,
            ReadOnlySpan<char> scope,
            Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> byDirectory)
    {
        var lengths = new int[kept.Count];

        for (var index = 0; index < lengths.Length; index++)
        {
            var directory = TopDirectory(kept[index], scope);

            lengths[index] = directory.Length;

            if (!directory.IsEmpty && !byDirectory.TryAdd(directory, 1))
                byDirectory[directory] += 1;
        }

        return lengths;
    }

    private static void Fold(
            List<string> kept,
            int[] lengths,
            Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> byDirectory,
            List<string> rows)
    {
        var folded = new HashSet<string>(StringComparer.Ordinal);
        var seen = folded.GetAlternateLookup<ReadOnlySpan<char>>();

        for (var index = 0; index < kept.Count; index++)
        {
            var directory = kept[index].AsSpan(0, lengths[index]);

            if (directory.IsEmpty || byDirectory[directory] <= UntrackedFold)
                rows.Add(kept[index] + "  +? -?  ?");
            else if (seen.Add(directory))
                rows.Add(string.Create(CultureInfo.InvariantCulture, $"{directory}/**  +? -?  ?  x{byDirectory[directory]} untracked"));
        }
    }

    private static ReadOnlySpan<char> Normalized(ReadOnlySpan<char> scope, Span<char> buffer)
    {
        for (var index = 0; index < scope.Length; index++)
            buffer[index] = scope[index] is '\\' ? '/' : scope[index];

        return buffer[..scope.Length];
    }
}
