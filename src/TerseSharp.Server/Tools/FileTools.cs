using System.Collections.Immutable;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class FileTools(ToolContext context)
{
    [McpServerTool(Name = "read_text")]
    [Description("Read any file, line-ranged. A .cs path asked for whole - no startLine, endLine or tail - answers with that file's outline plus a steer instead of its text, because the text is about three times the tokens and is almost never the question; pass verbose=true, or any line range, to get the text itself. The text is returned compressed: trailing whitespace is stripped and a line number is printed only where the numbering jumps, so a contiguous read carries one number. tail=N returns the last N lines, which is how a long log is read, and maxChars caps the file text on a file whose lines are very long. A clipped read names the line to continue from, and says so separately when a line had to be cut mid-way. On markdown, headings=true returns the heading map with line ranges, GitHub anchor slugs, and section=\"## Commands\" returns just that section. An absolute path outside every workspace root is read and tagged outside-workspace, so a cross-repo comparison needs no second load_workspace and no workspace= even when several are loaded.")]
    public Task<string> ReadText(
    [Description("Path, absolute or workspace-relative.")] string path,
    [Description("First line, 1-based. 0 = start of file.")] int startLine = 0,
    [Description("Last line, 1-based. 0 = end of file.")] int endLine = 0,
    [Description("Maximum lines returned, default 2000. The response is truncated, never refused.")] int maxLines = 0,
    [Description("Maximum characters of file text returned, default 40960 and at most 131072, and it bounds the text only - the line-number gutter, the notes and the count line are not charged to it. The default is set so a whole-file read stays inline in the client instead of being spilled to a file that answers nothing; a clipped read names the line to continue from. Raise it on a file you truly need whole, lower it on a file whose lines are very long, which maxLines cannot bound. Not applied to headings=true.")] int maxChars = 0,
    [Description("Return the last N lines instead of a range, the way tail -n does. Overrides startLine and endLine.")] int tail = 0,
    [Description("Markdown only: return the heading map (line ranges, no body) instead of the text.")] bool headings = false,
    [Description("Markdown only: return only this section, e.g. '## Commands'. The heading level is optional.")] string? section = null,
    [Description("Return the file verbatim - every line numbered, blank lines and trailing whitespace kept. On a .cs path this is also the opt-in that returns the text instead of the outline. Default false.")] bool verbose = false,
    [Description("Workspace or worktree name.")] string? workspace = null,
    CancellationToken cancellationToken = default)
    {
        var request = new FileService.ReadRequest(
            new FileService.LineRange(startLine, endLine, Lines(maxLines), Characters(maxChars)),
            headings,
            section,
            verbose,
            Math.Max(0, tail));
        return WholeCSharp(path, startLine, endLine, tail, maxLines, maxChars, section, headings, verbose)
            && !context.OutsideEveryWorkspace(path)
            ? context.WithWorkspaceAsync(
                workspace,
                path,
                async loaded => NavigationTools.Unwrap(
                    await OutlineService.OrTextAsync(loaded, path, request, cancellationToken).ConfigureAwait(false)),
                cancellationToken: cancellationToken)
            : Read(path, request, workspace, cancellationToken);
    }
    private static bool WholeCSharp(
    string path,
    int startLine,
    int endLine,
    int tail,
    int maxLines,
    int maxChars,
    string? section,
    bool headings,
    bool verbose) =>
    !verbose
    && !headings
    && section is null
    && (startLine, endLine, tail, maxLines, maxChars) is ( <= 0, <= 0, <= 0, <= 0, <= 0)
    && path.AsSpan().EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    private static int Characters(int requested) =>
        requested <= 0 ? FileService.DefaultResponseCharacters : Math.Min(requested, FileService.MaxResponseCharacters);

    private Task<string> Read(
        string path,
        FileService.ReadRequest request,
        string? workspace,
        CancellationToken cancellationToken) =>
        context.OutsideEveryWorkspace(path)
            ? ToolBoundary.RunAsync(async () => NavigationTools.Unwrap(
                await FileService.ReadOutsideAsync(path, request, cancellationToken).ConfigureAwait(false)))
            : context.WithWorkspaceAsync(
                workspace,
                path,
                async loaded => NavigationTools.Unwrap(
                    await FileService.ReadTextAsync(loaded, path, request, cancellationToken).ConfigureAwait(false)),
                semantic: false,
                cancellationToken);

    [McpServerTool(Name = "write_text")]
    [Description("Create or overwrite a file atomically, or delete one with delete=true. A successful write answers in one line - the file name and changedLines; pass verbose=true for the diff. A .cs file needs force=true, and when it is already a document in the workspace the write is compile-gated exactly like replace_symbol - rolled back if it introduces an error, unless allowErrors=true. That gate reads the workspace as it is now, so a file an earlier write_text created is already in the compilation and two new interdependent files land in either order. Deleting a .cs document goes through the same gate, so undo_last_change covers it. Missing directories are created, the file's existing line endings are kept, and the new or changed file is visible to every semantic tool of this workspace on the next call, with no reload. Another loaded workspace over the same root, and another terse process, pick the write up through their own file watcher instead, so their next call may still answer from the pre-write snapshot.")]
    public Task<string> WriteText(
    [Description("Path, absolute or workspace-relative.")] string path,
    [Description("Full new content. Omit it only with delete=true; an empty write needs allowEmpty=true, so a forgotten argument can never truncate a file.")] string? content = null,
    [Description("Delete the file instead of writing it. Refused on a path outside the workspace root, and on a .cs file without force=true.")] bool delete = false,
    [Description("Permit writing empty content, which truncates the file. Default false.")] bool allowEmpty = false,
    [Description("Diff only, write nothing.")] bool dryRun = false,
    [Description("Allow writing or deleting a .cs file. The write is still compile-gated when the file is already in the workspace.")] bool force = false,
    [Description("Apply a .cs write even if it introduces compile errors.")] bool allowErrors = false,
    [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
    [Description("Workspace or worktree name.")] string? workspace = null,
    CancellationToken cancellationToken = default) =>
    delete
        ? Guarded(workspace, path, async loaded => NavigationTools.Unwrap(
            await FileService.DeleteAsync(loaded, path, dryRun, force, cancellationToken).ConfigureAwait(false)), cancellationToken)
        : Written(workspace, path, content, allowEmpty, new WriteOptions(dryRun, force, allowErrors, verbose), cancellationToken);

    private Task<string> Written(
    string? workspace,
    string path,
    string? content,
    bool allowEmpty,
    WriteOptions options,
    CancellationToken cancellationToken) => content is { Length: > 0 } || (allowEmpty && content is not null)
    ? Guarded(workspace, path, async loaded => NavigationTools.Unwrap(await FileService.WriteTextAsync(
        loaded, path, content!, options.DryRun, options.Force, options.AllowErrors, options.Verbose, cancellationToken).ConfigureAwait(false)), cancellationToken)
    : Task.FromResult(Errors.Invalid(
        content is null ? "content was not supplied" : "content is empty, which would truncate the file",
        "pass the full new content; to truncate deliberately pass allowEmpty=true, to remove the file pass delete=true").Render());

    private readonly record struct WriteOptions(bool DryRun, bool Force, bool AllowErrors, bool Verbose);

    [McpServerTool(Name = "edit_text")]
    [Description("Replace a unique snippet in a file, or a whole markdown section with section=\"## Commands\". Pass edits=[{oldText,newText}, ...] to apply several edits to the SAME file as one write - each is applied in order to the result of the last, an edit whose anchor fails is reported on its own line with its error code and remedy while the rest still land, and at most 10 are taken. Line endings are normalized before matching, so a CRLF file accepts an LF oldText. Refuses when the match is not unique and names the file's closest lines with their line numbers; on a file of near-identical rows pass occurrence=N to pick the Nth match instead of lengthening the anchor. A successful edit answers in one line - the file name and changedLines; pass verbose=true for the diff.")]
    public Task<string> EditText(
    [Description("Path, absolute or workspace-relative.")] string path,
    [Description("Replacement text. With section=, this is the whole new section including its heading line. Omit it when edits= carries the edits.")] string? newText = null,
    [Description("Exact text to replace; must occur exactly once unless occurrence= picks one. Omit when section= is passed.")] string? oldText = null,
    [Description("Markdown only: replace this whole section, e.g. '## Commands'. No oldText needed.")] string? section = null,
    [Description("Diff only, write nothing.")] bool dryRun = false,
    [Description("Allow editing a .cs file, bypassing the compile-gated symbol tools. Default false.")] bool force = false,
    [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
    [Description("Workspace or worktree name.")] string? workspace = null,
    [Description("1-based index of the oldText match to replace when it deliberately occurs more than once. Default 0, which still requires exactly one match.")] int occurrence = 0,
    [Description("Several edits to this one file, applied in order as a single write: each entry takes oldText, newText and optionally section and occurrence. Cannot be combined with a top-level oldText, newText or section. Max 10.")] FileService.TextEdit[]? edits = null,
    CancellationToken cancellationToken = default) =>
    Guarded(
        workspace,
        path,
        loaded => EditedAsync(
            loaded,
            path,
            new FileService.EditRequest(oldText ?? string.Empty, newText ?? string.Empty, section, dryRun, force, verbose, occurrence),
            newText,
            edits,
            cancellationToken),
        cancellationToken);

    [McpServerTool(Name = "find_files")]
    [Description("Replaces Bash git ls-files. Locate files by glob under the workspace root, and with tracked=true only the files git tracks - which is how a checked-in fixture is told apart from build output or another session's scratch file. Use instead of Glob; bin, obj, .git, .claude, .vs, .idea, artifacts, TestResults, node_modules and directory symlinks are excluded. stamps=true adds each file's UTC last-write time and byte length, so \"when was this written, and how big is it?\" needs no shell.")]
    public Task<string> FindFiles(
[Description("Glob such as *.csproj, *Tests.cs, or a path glob like **/Views/*.xaml. ** spans directories, * and ? stop at a separator.")] string? glob = null,
[Description("Workspace or worktree name.")] string? workspace = null,
[Description("Max results (100).")] int maxResults = 0,
[Description("Alias for glob.")] string? pattern = null,
[Description("Append each listed file's UTC last-write time and byte length. Default false.")] bool stamps = false,
[Description("Alias for glob.")] string? query = null,
[Description("List only the files git tracks, so build output and untracked scratch files drop out. Needs a git repository. Default false.")] bool tracked = false,
CancellationToken cancellationToken = default) =>
(glob ?? pattern ?? query) is { Length: > 0 } matched
    ? context.WithWorkspaceAsync(
        workspace,
        null,
        loaded => ListedAsync(loaded, matched, NavigationTools.Cap(maxResults, 100), stamps, tracked, cancellationToken),
        semantic: false,
        cancellationToken)
    : Task.FromResult(Errors.Blank("glob", "pattern", "query").Render());

    [McpServerTool(Name = "search_text")]
    [Description("Literal text search across the workspace, or across any absolute directory with root=. queries=[...] searches up to 10 literals in ONE pass over the same file set, tagging every record q1..qN by the position of the literal in queries=, which replaces one call per literal and the shell grep alternation that cannot say which alternative matched. A line matching several of them is ONE record carrying all of their tags, comma-separated in query order (q1,q3), so a tag missing from a record really does mean that literal is absent from that line. Also the counting tool: the count line is how many matching LINES exist, at most one per line, and a zero result proves absence in the files it searched - bin, obj, .git, .claude, .vs, .idea, artifacts, TestResults, node_modules and directory symlinks are skipped. context=N adds the surrounding lines so a hit needs no follow-up read, matchesOnly=true prints the matched span instead of the whole line the way grep -o does, unique=true collapses identical matching lines to one record with x<count>, and exclude= drops the paths a glob= cannot leave out. Results are tagged HEURISTIC: for a type or member name use search_symbols or find_usages instead.")]
    public Task<string> SearchText(
    [Description("Literal text to find.")] string? query = null,
    [Description("Optional file glob, e.g. *.json or **/Views/*.xaml. ** spans directories, * and ? stop at a separator.")] string? glob = null,
    [Description("Workspace or worktree name.")] string? workspace = null,
    [Description("Max results (100).")] int maxResults = 0,
    [Description("Lines of surrounding context per hit, 0-5. Default 0, which returns the matching line only.")] int context = 0,
    [Description("Collapse identical matching lines to one record carrying x<count>. Use on logs and generated output.")] bool unique = false,
    [Description("Absolute directory to search instead of the workspace, e.g. a log folder. The answer is tagged outside-workspace.")] string? root = null,
    [Description("Alias for query.")] string? pattern = null,
    [Description("Glob of paths to drop after glob= has selected them, e.g. .research/** or **/*.generated.cs.")] string? exclude = null,
    [Description("Print the matched span instead of the whole line, the way grep -o does, and compose it with unique=true to answer which distinct values of this shape exist. A match that is only whitespace still prints its line, so no record is ever empty. Default false.")] bool matchesOnly = false,
    [Description("Several literals answered in one pass over the same file set, at most 10. Every record is tagged q1..qN by the position of its literal here, so one call answers where are these N things and no legend is echoed back. A line matching several literals is ONE record tagged with all of them, comma-separated in query order. Combines with query, which is taken first; more than 10 entries is refused rather than truncated.")] string?[]? queries = null,
    CancellationToken cancellationToken = default) =>
    Search(new TextQuery(query ?? pattern, glob, workspace, maxResults, Regex: false, context, unique, root, exclude, matchesOnly, queries), cancellationToken);

    [McpServerTool(Name = "search_regex")]
    [Description("Regular-expression search across the workspace, or across any absolute directory with root=. queries=[...] searches up to 10 expressions in ONE pass over the same file set, tagging every record q1..qN by the position of the expression in queries=, which is what an alternation cannot do: it returns one undifferentiated list. A line matching several of them is ONE record carrying all of their tags, comma-separated in query order (q1,q3). The count line is how many matching LINES exist, at most one per line, and a zero result proves absence in the files it searched - bin, obj, .git, .claude, .vs, .idea, artifacts, TestResults, node_modules and directory symlinks are skipped. ^ and $ anchor each line, and a match that spans several lines is reported once, at the first line carrying its text. context=N adds the surrounding lines so a hit needs no follow-up read, matchesOnly=true prints the matched span instead of the whole line the way grep -o does, unique=true collapses identical matching lines to one record with x<count>, and exclude= drops the paths a glob= cannot leave out. Results are tagged HEURISTIC.")]
    public Task<string> SearchRegex(
    [Description(".NET regular expression.")] string? query = null,
    [Description("Optional file glob, e.g. *.cs or **/Views/*.xaml. ** spans directories, * and ? stop at a separator.")] string? glob = null,
    [Description("Workspace or worktree name.")] string? workspace = null,
    [Description("Max results (100).")] int maxResults = 0,
    [Description("Lines of surrounding context per hit, 0-5. Default 0, which returns the matching line only.")] int context = 0,
    [Description("Collapse identical matching lines to one record carrying x<count>. Use on logs and generated output.")] bool unique = false,
    [Description("Absolute directory to search instead of the workspace, e.g. a log folder. The answer is tagged outside-workspace.")] string? root = null,
    [Description("Alias for query.")] string? pattern = null,
    [Description("Glob of paths to drop after glob= has selected them, e.g. .research/** or **/*.generated.cs.")] string? exclude = null,
    [Description("Print the matched span instead of the whole line, the way grep -o does, and compose it with unique=true to answer which distinct values of this shape exist. A match that is only whitespace still prints its line, so no record is ever empty. Default false.")] bool matchesOnly = false,
    [Description("Several expressions answered in one pass over the same file set, at most 10. Every record is tagged q1..qN by the position of its expression here, so the caller can tell which expression produced which record - which one alternation cannot. A line matching several expressions is ONE record tagged with all of them, comma-separated in query order. Combines with query, which is taken first; more than 10 entries is refused rather than truncated.")] string?[]? queries = null,
    CancellationToken cancellationToken = default) =>
    Search(new TextQuery(query ?? pattern, glob, workspace, maxResults, Regex: true, context, unique, root, exclude, matchesOnly, queries), cancellationToken);

    private Task<string> Search(TextQuery request, CancellationToken cancellationToken)
    {
        var requested = Requested(request);

        return requested.IsOk
            ? Scanned(request, Scoped(request, requested.Value), cancellationToken)
            : Task.FromResult(requested.Error!.Render());
    }

    private static Result<ImmutableArray<string>> Requested(TextQuery request)
    {
        var patterns = ImmutableArray.CreateBuilder<string>();

        if (request.Text is { Length: > 0 } text)
            patterns.Add(text);

        foreach (var entry in request.Texts ?? [])
        {
            if (entry is not { Length: > 0 })
                return Result.Fail<ImmutableArray<string>>(Errors.Invalid("'queries' carries a blank entry", "drop it, or pass the literal you meant to search for"));

            patterns.Add(entry);
        }

        return Verified(patterns.DrainToImmutable());
    }

    private static Result<ImmutableArray<string>> Verified(ImmutableArray<string> patterns) => patterns switch
    {
        [] => Result.Fail<ImmutableArray<string>>(Errors.Blank("query", "pattern", "queries")),
        { Length: > TextSearchRequest.MaxPatterns } => Result.Fail<ImmutableArray<string>>(Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"{patterns.Length} patterns were requested - query plus queries - more than the {TextSearchRequest.MaxPatterns} one pass answers"),
            string.Create(CultureInfo.InvariantCulture, $"send at most {TextSearchRequest.MaxPatterns} per call, or narrow the file set with glob="))),
        _ => Result.Ok(patterns),
    };


    private static TextSearchRequest Scoped(TextQuery request, ImmutableArray<string> patterns) => new(
        patterns,
        request.Glob ?? "*",
        request.Regex,
        NavigationTools.Cap(request.MaxResults, 100),
        request.Context,
        request.Unique,
        request.Root,
        request.Exclude,
        request.MatchesOnly);


    private Task<string> Scanned(TextQuery request, TextSearchRequest search, CancellationToken cancellationToken) =>
        request.Root is { Length: > 0 }
            ? TextSearchService.SearchOutsideAsync(search, cancellationToken)
            : context.WithWorkspaceAsync(
                request.Workspace,
                null,
                loaded => TextSearchService.SearchAsync(loaded, search, cancellationToken),
                semantic: false,
                cancellationToken);

    private readonly record struct TextQuery(
    string? Text,
    string? Glob,
    string? Workspace,
    int MaxResults,
    bool Regex,
    int Context = 0,
    bool Unique = false,
    string? Root = null,
    string? Exclude = null,
    bool MatchesOnly = false,
    IReadOnlyList<string?>? Texts = null);

    private Task<string> Guarded(
    string? workspace,
    string path,
    Func<LoadedWorkspace, Task<string>> action,
    CancellationToken cancellationToken = default) =>
    context.RejectWrite() is { } rejection
        ? Task.FromResult(rejection)
        : context.WithWorkspaceAsync(workspace, path, action, SourceFile.IsCSharp(path), cancellationToken);

    private static int Lines(int requested) => requested <= 0 ? 2000 : Math.Min(requested, 20000);

    private static async Task<Result<HashSet<string>>> TrackedAsync(
    LoadedWorkspace loaded,
    CancellationToken cancellationToken)
    {
        var listed = await GitRunner.ReadAsync(
            loaded.Root,
            ["--no-optional-locks", "-c", "core.quotePath=false", "ls-files", "--cached"],
            cancellationToken).ConfigureAwait(false);

        if (!listed.IsOk)
            return Result.Fail<HashSet<string>>(listed.Error!);

        var tracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in listed.Value!.AsSpan().EnumerateLines())
        {
            var path = line.Trim();

            if (!path.IsWhiteSpace())
                tracked.Add(Hosted(path));
        }

        return Result.Ok(tracked);
    }

    private static string Hosted(ReadOnlySpan<char> path)
    {
        if (Path.DirectorySeparatorChar is '/' || !path.Contains('/'))
            return new string(path);

        var buffer = path.Length <= MaxStackPath ? stackalloc char[MaxStackPath] : new char[path.Length];
        var target = buffer[..path.Length];

        path.CopyTo(target);
        target.Replace('/', Path.DirectorySeparatorChar);

        return new string(target);
    }

    private const int MaxStackPath = 512;

    private static async Task<string> ListedAsync(
        LoadedWorkspace loaded,
        string glob,
        int maxResults,
        bool stamps,
        bool tracked,
        CancellationToken cancellationToken)
    {
        if (!tracked)
            return TextSearchService.FindFiles(loaded, glob, maxResults, stamps);

        var known = await TrackedAsync(loaded, cancellationToken).ConfigureAwait(false);

        return known.IsOk
            ? TextSearchService.FindFiles(loaded, glob, maxResults, stamps, known.Value!)
            : known.Error!.Render();
    }

    private const int MaxBatchedEdits = 10;

    private static async Task<string> EditedAsync(
    LoadedWorkspace loaded,
    string path,
    FileService.EditRequest request,
    string? newText,
    FileService.TextEdit[]? edits,
    CancellationToken cancellationToken)
    {
        if (edits is not { Length: > 0 } batch)
        {
            return newText is null
                ? Errors.Blank("newText", "edits").Render()
                : NavigationTools.Unwrap(await FileService.EditTextAsync(loaded, path, request, cancellationToken).ConfigureAwait(false));
        }

        if (newText is not null || request.OldText is { Length: > 0 } || request.Section is { Length: > 0 })
        {
            return Errors.Invalid(
                "edits= was passed together with a top-level oldText, newText or section, and the top-level edit would have been silently dropped",
                "put every edit in edits=, or send the single edit without edits=").Render();
        }

        if (batch.Length > MaxBatchedEdits)
        {
            return Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"edits carried {batch.Length} entries, at most {MaxBatchedEdits} are applied as one write"),
                "split it into smaller calls - batched-item accuracy falls off past about six edits").Render();
        }

        return NavigationTools.Unwrap(await FileService.EditTextBatchAsync(loaded, path, batch, request, cancellationToken).ConfigureAwait(false));
    }
}
