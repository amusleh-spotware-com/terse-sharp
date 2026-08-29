using System.Collections.Immutable;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class FileTools(ToolContext context)
{
    [McpServerTool(Name = "read_text", ReadOnly = true)]
    [Description("Read any file, line-ranged. Pass paths to read up to 10 files in ONE response. Replaces one call per file: each is rendered under its own path line with its own count and continuation note, a path that does not resolve is reported inline as NOT_FOUND instead of failing the call, and maxChars is a budget shared across the batch that names the entry it clipped. ref= reads the file as it was at a git ref instead of shelling out for it, and a whole .cs file there answers its outline the same way the working-tree read does. A .cs path asked for whole - no startLine, endLine or tail - answers with that file's outline plus a steer instead of its text, because the text is about three times the tokens and is almost never the question; pass verbose=true, or any line range, to get the text itself. A markdown file over 8000 characters asked for whole answers its SECTION MAP plus the same steer; verbose=true, a line range, tail=, section= or columns= opt back into the text. The text is returned compressed: trailing whitespace is stripped and a line number is printed only where the numbering jumps, so a contiguous read carries one number. tail=N returns the last N lines, which is how a long log is read, and maxChars caps the file text on a file whose lines are very long. A clipped read names the line to continue from, and says so separately when a line had to be cut mid-way. tokens=true ends it with the whole file's tokens=N, the count the doc budgets assert. On markdown, headings=true returns the heading map with line ranges and GitHub anchor slugs - bounded by maxLines, and narrowed to the top of the tree by maxLevel=2, which lists the ## sections without their ### children - and section=\"## Commands\" returns just that section, and columns=\"Finding,Tool\" projects a markdown table down to the named columns, so a checked-in table answers which rows exist without paying for the whole file - it composes with section=, is bounded by maxLines, and a column no table under the read declares is refused by name rather than dropped, as are headings=true, startLine=, endLine= and tail= beside it. An absolute path outside every workspace root is read and tagged outside-workspace, so a cross-repo comparison needs no second load_workspace and no workspace= even when several are loaded.")]
    public Task<string> ReadText(
                [Description("Path, absolute or workspace-relative.")] string? path = null,
                [Description("Several files answered in one response, at most 10. Replaces one call per file. Combines with path, which is taken first; a blank entry and an 11th entry are refused by name rather than dropped.")] string?[]? paths = null,
                [Description("First line, 1-based. 0 = start of file.")] int startLine = 0,
                [Description("Last line, 1-based. 0 = end of file.")] int endLine = 0,
                [Description("Maximum lines returned, default 2000. The response is truncated, never refused. With headings=true it bounds the SECTIONS listed, and the summary says how many of the total were shown.")] int maxLines = 0,
                [Description("Maximum characters of file text returned, default 40960 at most 131072. It bounds the file text only, is one budget shared across a paths= batch, and does not apply to headings=true; a clipped read names the line to continue from.")] int maxChars = 0,
                [Description("Return the last N lines instead of a range, the way tail -n does. Overrides startLine and endLine.")] int tail = 0,
                [Description("End the answer with the file's byte length as bytes=N, on every shape read_text returns and once per paths= entry. Under ref= it is the blob's size at that revision; one that cannot be sized answers bytes=UNRESOLVED. Default false.")] bool bytes = false,
                [Description("End the answer with the whole file's token cost as tokens=N, whatever range was read. Default false.")] bool tokens = false,
                [Description("Markdown only: return the heading map (line ranges, no body) instead of the text.")] bool headings = false,
                [Description("With headings=true, the deepest heading level to list: 2 keeps # and ## and drops every ###. Anchors stay the ones GitHub assigns. Default 0, every level. Refused without headings=true.")] int maxLevel = 0,
                [Description("Markdown only: return only this section, e.g. '## Commands'. The heading level is optional.")] string? section = null,
                [Description("With section=, the 1-based index of the section when that heading repeats - '### Added' once per release. Default 0, which requires exactly one match; a multi-match refusal names each candidate's start line.")] int occurrence = 0,
                [Description("Markdown only: comma-separated column headers to project every markdown table row down to, e.g. 'Finding,Tool'. Which rows a checked-in table holds, without the whole table. Composes with section=, maxLines= and maxChars=; a column no table under the read declares, and headings=/startLine=/endLine=/tail= beside it, are refused by name.")] string? columns = null,
                [Description("Return the file verbatim - every line numbered, blank lines and trailing whitespace kept. On a .cs path this is also the opt-in that returns the text instead of the outline. Default false.")] bool verbose = false,
                [Description("Git ref to read the file at, e.g. main or HEAD~3; the historical text gets the same gutter, ranges, tail=, section= and maxChars as the working tree, and a whole .cs file answers its outline. Takes one path.")] string? @ref = null,
                [Description("Workspace or worktree name.")] string? workspace = null,
                CancellationToken cancellationToken = default)
    {
        var combined = PluralPaths.Combine(path, paths, "paths");

        if (!combined.IsOk)
            return Task.FromResult(combined.Error!.Render());

        if (Refused(section, occurrence, headings, maxLevel) is { } refusal)
            return Task.FromResult(refusal.Render());

        var request = new FileService.ReadRequest(
            new FileService.LineRange(startLine, endLine, Lines(maxLines), Characters(maxChars)),
            headings,
            section,
            verbose,
            Math.Max(0, tail),
            bytes,
            Columns: Columned(columns),
            Occurrence: Math.Max(0, occurrence),
            MaxLevel: Math.Max(0, maxLevel),
            Tokens: tokens);

        var whole = WholeRead(startLine, endLine, tail, maxLines, maxChars, section, headings, verbose) && columns is null;

        if (@ref is { Length: > 0 } reference)
        {
            return combined.Value is [var only]
                ? context.WithWorkspaceAsync(
                    workspace,
                    only,
                    loaded => RefRead.TextAsync(loaded, only, reference, request, whole, cancellationToken),
                    semantic: false,
                    cancellationToken)
                : Task.FromResult(RefRead.Batched("paths=").Render());
        }

        return combined.Value is [var single]
            ? ReadOneAsync(single, request, whole, workspace, cancellationToken)
            : ReadManyAsync(combined.Value, request, whole, workspace, cancellationToken);
    }
    private static bool WholeRead(
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
&& (startLine, endLine, tail, maxLines, maxChars) is ( <= 0, <= 0, <= 0, <= 0, <= 0);
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
    [Description("Create or overwrite a file atomically, delete one with delete=true, or restore one from a git ref with ref=HEAD - which is the way back from a bad write to a file undo_last_change cannot cover, because its history holds Roslyn snapshots and a .csproj or .md write is not one. Pass files=[{path,content}, ...] to write up to 10 files in ONE call, and the top-level force=true covers every .cs entry among them, so one batch writes a fixture of mixed kinds. Replaces one call per file: every .cs document among them goes through ONE compile gate, so a type and the consumer it breaks land together instead of the first write being rolled back on its own, and a rollback names which file introduced the error and writes nothing at all. A successful write answers in one line per file - the file name and changedLines; pass verbose=true for the diff. An entry with empty content is refused exactly as a single write is, unless allowEmpty=true. A .cs file needs force=true, and the write is compile-gated exactly like replace_symbol when it is already a document OR is NEW under an SDK-globbing project - rolled back if it introduces an error, unless allowErrors=true. A .cs file no project globs stays ungated. force=true also lets a SINGLE write land on an absolute path outside every workspace root, tagged outside-workspace and never compile-gated. That gate reads the workspace as it is now, so a file an earlier write_text created is already in the compilation and two new interdependent files land in either order. Deleting a .cs document goes through the same gate, so undo_last_change covers it. delete=true on a path that is an EMPTY DIRECTORY removes that directory - the one file-system action no tool here served, so a folder a delete left behind no longer needs a shell - and a non-empty one is refused naming what it still holds. Missing directories are created, the file's existing line endings are kept, and the new or changed file is visible to every semantic tool of this workspace on the next call, with no reload. Another loaded workspace over the same root, and another terse process, pick the write up through their own file watcher instead, so their next call may still answer from the pre-write snapshot.")]
    public Task<string> WriteText(
        [Description("Path, absolute or workspace-relative. An absolute path outside every workspace root is written only with force=true.")] string? path = null,
        [Description("Full new content. Omit only with delete=true or ref=; an empty write needs allowEmpty=true.")] string? content = null,
        [Description("Several files written in one call, at most 10, each entry taking path and content. Replaces one call per file, and every .cs document among them shares one compile gate. The top-level force= covers every entry; an entry outside the workspace root, and one whose content is empty without allowEmpty=true, are refused. Not with a top-level path, content, ref or delete=true.")] FileService.FileWrite[]? files = null,
        [Description("Delete the file instead of writing it. Refused on a path outside the workspace root, and on a .cs file without force=true.")] bool delete = false,
        [Description("Git ref to restore the file's content from, e.g. HEAD. Not with content, files or delete; the restored write is gated like any other.")] string? @ref = null,
        [Description("Permit writing empty content, which truncates the file. Default false.")] bool allowEmpty = false,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Allow writing or deleting a .cs file, every files= entry included, and a single write outside every workspace root. Still compile-gated when a project compiles it.")] bool force = false,
        [Description("Apply a .cs write even if it introduces compile errors.")] bool allowErrors = false,
        [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Apply a write the .terse.json code policy would reject; the response names every rule it bypassed. Default false.")] bool allowPolicy = false,
        CancellationToken cancellationToken = default)
    {
        if (@ref is { Length: > 0 } && (delete || content is not null || files is { Length: > 0 }))
        {
            return Task.FromResult(Errors.Invalid(
                "ref= restores one file's content, so it cannot be combined with content, files or delete",
                "pass ref with path alone to restore, or content alone to write new text").Render());
        }

        return files is { Length: > 0 } batch
            ? WrittenMany(workspace, path, content, delete, allowEmpty, batch, new WriteOptions(dryRun, force, allowErrors, verbose, allowPolicy), cancellationToken)
            : WrittenOne(workspace, path, content, delete, @ref, allowEmpty, new WriteOptions(dryRun, force, allowErrors, verbose, allowPolicy), cancellationToken);
    }

    private Task<string> Written(
    string? workspace,
    string path,
    string? content,
    bool allowEmpty,
    WriteOptions options,
    CancellationToken cancellationToken)
    {
        if (content is null || (content.Length is 0 && !allowEmpty))
        {
            return Task.FromResult(Errors.Invalid(
                content is null ? "content was not supplied" : "content is empty, which would truncate the file",
                "pass the full new content; to truncate deliberately pass allowEmpty=true, to remove the file pass delete=true").Render());
        }

        if (Foreign(workspace, path) is { } foreign)
            return Task.FromResult(foreign.Render());

        return Guarded(workspace, path, async loaded => NavigationTools.Unwrap(await FileService.WriteTextAsync(
            loaded, path, content, options.DryRun, options.Force, options.AllowErrors, options.Verbose, options.AllowPolicy, cancellationToken).ConfigureAwait(false)), cancellationToken: cancellationToken);
    }

    private readonly record struct WriteOptions(bool DryRun, bool Force, bool AllowErrors, bool Verbose, bool AllowPolicy = false);

    [McpServerTool(Name = "edit_text")]
    [Description("Replace a unique snippet in a file, or a whole markdown section with section=\"## Commands\" - and with place=append or place=prepend, write INSIDE that section instead of replacing it, so a new changelog entry needs no oldText. With toPath=, section= MOVES the section into another markdown file in one write, and row=\"I286\" moves ONE table row, matched by its first cell, so the row costs its identifier instead of its text; rows=[{row,newText}, ...] moves up to 25 of them as ONE write per file. Pass edits=[{oldText,newText}, ...] to apply several edits in one call. Replaces one call per edit: entries without a path go to the top-level path and are applied in order as a single write, an entry may carry its own path to edit ANOTHER file in the same call - grouped by file, one write and one answer line per file - and an edit whose anchor fails is reported on its own line with its error code and remedy while the rest still land. The top-level path may be omitted entirely when every entry declares its own. At most 10 entries per file and 25 in total. Line endings are normalized before matching, so a CRLF file accepts an LF oldText. Refuses when the match is not unique and names the file's closest lines with their line numbers; on a file of near-identical rows pass occurrence=N to pick the Nth match instead of lengthening the anchor. On a .cs file, force=true is the sanctioned way to amend a declaration's ATTRIBUTES - a tool [Description], an [Obsolete] - since a short anchor costs ~30 tokens where replace_symbol re-sends the whole declaration; it is NOT compile-gated, so analyze the file after. A successful edit answers in one line per changed file - the file name and changedLines; pass context=N for the N lines around each change in their POST-edit state, which is the confirm-read the one-line answer otherwise costs, and verbose=true for the diff.")]
    public Task<string> EditText(
        [Description("Path, absolute or workspace-relative. With edits=, the default target of every entry that carries no path of its own - and it may be omitted entirely when every entry declares one.")] string? path = null,
        [Description("Replacement text. With section=, the whole new section including its heading line, unless place= writes inside it. With row=, the row as it should read in the target; empty moves it verbatim. Omit it when edits= carries the edits.")] string? newText = null,
        [Description("Exact text to replace; must occur exactly once unless occurrence= picks one. Omit when section= is passed.")] string? oldText = null,
        [Description("Markdown only: replace this whole section, e.g. '## Commands'. No oldText needed. With place=, written inside; with toPath=, cut and written into that file. A repeating heading is picked with occurrence=.")] string? section = null,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Allow editing a .cs file, bypassing the compile-gated symbol tools. Default false.")] bool force = false,
        [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("1-based index of the match to replace when it deliberately repeats - the oldText match, or beside section= that heading. Default 0 requires exactly one; a multi-match refusal lists the candidates.")] int occurrence = 0,
        [Description("With section=, lowercase: append writes after its last non-blank line, prepend directly under its heading. With toPath=, prepend puts the moved section at the top of the target, anything else appends it. Empty replaces the section.")] string? place = null,
        [Description("Markdown only, with section=, row= or rows=: an EXISTING file to MOVE that section or those rows into. Cut from path, written into toPath as one write, one line per changed file. Naming the same file twice is refused. Not with oldText or edits.")] string? toPath = null,
        [Description("Markdown only, with toPath=: the identifier of ONE table row to move, matched against the first cell of each row - e.g. row=\"I286\". It must match exactly one, and the refusal says how many it matched. Not with section=, oldText or edits.")] string? row = null,
        [Description("Several edits applied in one call: each entry takes oldText, newText and optionally section, occurrence, place and path. Entries sharing a path are applied in order as one write to it; path defaults to the top-level path, which may be omitted when every entry carries its own. Cannot be combined with a top-level oldText, newText or section. Max 10 per file, 25 in total.")] FileService.TextEdit[]? edits = null,
        [Description("Markdown only, with toPath=: several table rows moved in ONE call, at most 25, each taking row and optionally newText. Replaces one call per row: cut and landed in order, written once per file. Not with row=, section=, oldText or edits.")] FileService.TextRow[]? rows = null,
        [Description("Return the N lines around each applied change in their POST-edit state, numbered, instead of only changedLines - the confirm-read a successful edit otherwise costs. 1-10, refused outside that range; 0 (default) returns nothing extra. It is NOT a diff, which stays behind verbose=true, and verbose=true returns the diff instead of the window.")] int context = 0,
        CancellationToken cancellationToken = default)
    {
        var anchor = Anchor(path, edits);

        if (!anchor.IsOk)
            return Task.FromResult(anchor.Error!.Render());

        if (Bounded(context) is { } refusal)
            return Task.FromResult(refusal.Render());

        var target = anchor.Value!;

        return Guarded(
            workspace,
            target,
            loaded => EditedAsync(
                loaded,
                path ?? target,
                new FileService.EditRequest(oldText ?? string.Empty, newText ?? string.Empty, section, dryRun, force, verbose, occurrence, place, toPath, row, context),
                newText,
                edits,
                rows,
                cancellationToken),
            TouchesCSharp(target, edits),
            cancellationToken);
    }

    private const int MaxEditContext = 10;

    private static TerseError? Bounded(int context) => context is >= 0 and <= MaxEditContext
        ? null
        : Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"context={context} is outside 0-{MaxEditContext}"),
            string.Create(CultureInfo.InvariantCulture, $"pass 1-{MaxEditContext} lines of context, or 0 for the one-line answer"));

    private static Result<string> Anchor(string? path, FileService.TextEdit[]? edits)
    {
        if (path is { Length: > 0 })
            return Result.Ok(path);

        if (edits is not { Length: > 0 } batch)
        {
            return Result.Fail<string>(Errors.Invalid(
                "'path' is required and cannot be empty",
                "pass the file to edit, or pass edits=[...] where every entry declares its own path"));
        }

        var missing = Array.FindIndex(batch, edit => edit.Path is not { Length: > 0 });

        return missing < 0
            ? Result.Ok(batch[0].Path!)
            : Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"edits[{missing}] carries no path and no top-level path was passed"),
                "pass path= as the default target for the entries that have none, or give every entry its own path"));
    }

    [McpServerTool(Name = "find_files", ReadOnly = true)]
    [Description("Replaces Bash git ls-files. Locate files by glob under the workspace root, or by name= for a plain file-name substring that needs no glob syntax at all, and with tracked=true only the files git tracks - which is how a checked-in fixture is told apart from build output or another session's scratch file. Use instead of Glob; bin, obj, .git, .claude, .vs, .idea, artifacts, TestResults, node_modules and directory symlinks are excluded. name= combines with glob=, which selects first, and a glob that matched nothing is told to try it. stamps=true adds each file's UTC last-write time and byte length, so \"when was this written, and how big is it?\" needs no shell. depth=N answers the shape of a tree instead of its files: everything below the Nth path segment folds into one src/Core/**  xN files row, and the count line still counts every file. root= lists any absolute directory instead of the workspace, tagged outside-workspace, so no shell ls is needed. Pass globs to answer up to 10 globs in ONE response. Replaces one call per glob: each is answered under its own header line with its own count.")]
    public Task<string> FindFiles(
            [Description("Glob such as *.csproj, *Tests.cs, or a path glob like **/Views/*.xaml. ** spans directories, * and ? stop at a separator, and {a,b} matches either alternative.")] string? glob = null,
            [Description("Workspace or worktree name.")] string? workspace = null,
            [Description("Max results (100).")] int maxResults = 0,
            [Description("Alias for glob.")] string? pattern = null,
            [Description("Append each listed file's UTC last-write time and byte length. Default false.")] bool stamps = false,
            [Description("Alias for glob.")] string? query = null,
            [Description("Alias for glob.")] string? path = null,
            [Description("List only the files git tracks. Needs a git repository. Default false.")] bool tracked = false,
            [Description("Keep only the files whose FILE NAME contains this text, case-insensitively - no glob to get right. Used alone it searches every file; with glob= it filters what the glob selected.")] string? name = null,
            [Description("Fold every file below the Nth path segment into one directory row with its file count, so the answer is the shape of the tree. A directory with a single match stays that file. 0, the default, lists every file.")] int depth = 0,
            [Description("Absolute directory to list instead of the workspace, tagged outside-workspace; its paths= line carries full paths. Refused beside tracked=true.")] string? root = null,
            [Description("Several globs in ONE response, at most 10. Replaces one call per glob: each gets its own header line and count, so a glob that matched nothing is visible. Combines with glob, taken first.")] string?[]? globs = null,
            CancellationToken cancellationToken = default)
    {
        var matcher = glob ?? pattern ?? query ?? path;

        if (matcher is not { Length: > 0 } && name is not { Length: > 0 } && globs is not { Length: > 0 })
        {
            return Task.FromResult(Errors.Invalid(
                "neither 'glob' nor 'name' was supplied",
                "pass a glob, spelled glob or pattern or query or path, or 'name' to match a file name substring instead").Render());
        }

        if (globs is { Length: > 0 } && root is { Length: > 0 })
        {
            return Task.FromResult(Errors.Invalid(
                "'globs' was passed with 'root', and a batch answers about the loaded workspace only",
                "drop root= to answer several globs over the workspace, or pass one glob= beside root= for that directory").Render());
        }

        if (depth < 0)
        {
            return Task.FromResult(Errors.Invalid(
                "'depth' was negative",
                "pass depth=0 to list every file, or a positive segment count such as depth=2 to fold below it").Render());
        }

        if (root is { Length: > 0 } directory)
        {
            return Task.FromResult(tracked
                ? Errors.Invalid(
                    "'tracked' was passed with 'root', and this tool loads no repository for that directory",
                    "drop tracked=true to list every file under root=, or drop root= to list the tracked files of the workspace").Render()
                : NavigationTools.Unwrap(TextSearchService.FindFilesOutside(
                    directory,
                    matcher ?? string.Empty,
                    NavigationTools.Cap(maxResults, 100),
                    stamps,
                    name,
                    depth,
                    maxResults > 0)));
        }

        if (globs is not { Length: > 0 })
        {
            return context.WithWorkspaceAsync(
                workspace,
                null,
                loaded => ListedAsync(loaded, matcher ?? string.Empty, NavigationTools.Cap(maxResults, 100), stamps, tracked, name, depth, maxResults > 0, cancellationToken),
                semantic: false,
                cancellationToken);
        }

        var combined = PluralPaths.Combine(matcher, globs, "globs");

        if (!combined.IsOk)
            return Task.FromResult(combined.Error!.Render());

        return combined.Value is [var single]
            ? context.WithWorkspaceAsync(
                workspace,
                null,
                loaded => ListedAsync(loaded, single, NavigationTools.Cap(maxResults, 100), stamps, tracked, name, depth, maxResults > 0, cancellationToken),
                semantic: false,
                cancellationToken)
            : ManyAsync(combined.Value, workspace, maxResults, stamps, tracked, name, depth, cancellationToken);
    }

    [McpServerTool(Name = "search_text", ReadOnly = true)]
    [Description("Literal text search across the workspace, or across any absolute directory with root=. Pass queries to search up to 10 literals in ONE pass over the same file set. Replaces one call per literal, and the shell grep alternation that cannot say which alternative matched, because every record is tagged q1..qN by the position of its literal in queries=. A line matching several of them is ONE record carrying all of their tags, comma-separated in query order (q1,q3). An entry that matches across a line break is reported once, at the line its text starts on, and the scan resumes on the next line, so every other entry still sees the lines it spanned. Also the counting tool: the count line is how many matching LINES exist, at most one per line, and a zero result proves absence in the files it searched - bin, obj, .git, .claude, .vs, .idea, artifacts, TestResults, node_modules and directory symlinks are skipped. countOnly=true answers ONE line per file with its match count and no matched text, tagged q1=N per query, which is what a presence check across a large tree costs instead of the matching lines themselves. context=N adds the surrounding lines so a hit needs no follow-up read, unique=true collapses identical matching lines to one record with x<count>, and exclude= drops the paths a glob= cannot leave out. matchesOnly= belongs to search_regex and is refused here, because a literal's matched span is the literal you passed. containers=true names the C# declaration each hit sits in, so a hit is an id get_symbol_source takes rather than a coordinate you must outline first, and word=true keeps a literal only where neither side is a letter, a digit or an underscore. Results are tagged HEURISTIC: for a type or member name use search_symbols or find_usages instead.")]
    public Task<string> SearchText(
        [Description("Literal text to find.")] string? query = null,
        [Description("Optional file glob, e.g. *.json or **/Views/*.xaml. ** spans directories, * and ? stop at a separator.")] string? glob = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0,
        [Description("Lines of surrounding context per hit, 0-5. Default 0, which returns the matching line only.")] int context = 0,
        [Description("Collapse identical matching lines to one record carrying x<count>. Use on logs and generated output.")] bool unique = false,
        [Description("Absolute directory to search instead of the workspace, e.g. a log folder. The answer is tagged outside-workspace.")] string? root = null,
        [Description("Alias for query.")] string? pattern = null,
        [Description("Alias for glob.")] string? path = null,
        [Description("Glob of paths to drop after glob= has selected them, e.g. .research/** or **/*.generated.cs.")] string? exclude = null,
        [Description("Refused here: a literal's matched span is the literal you passed. Use search_regex, or countOnly=true for one count per file.")] bool matchesOnly = false,
        [Description("One line per file - path and its match count, q1=N per query - and no matched text. Refused beside matchesOnly=, unique= and context=. Default false.")] bool countOnly = false,
        [Description("Pass queries to search several literals in one pass over the same file set, at most 10. Replaces one call per literal; every record is tagged q1..qN by the position of its literal here. Combines with query, which is taken first.")] string?[]? queries = null,
        [Description("Name the C# declaration each hit sits in - Type.Member, from syntax - so the record is an id get_symbol_source takes. .cs only; refused beside countOnly=. Default false.")] bool containers = false,
        [Description("Match whole words only: a literal is kept only where neither side is a letter, a digit or an underscore. Applies to query= and every queries= entry. Default false.")] bool word = false,
        CancellationToken cancellationToken = default) =>
        Search(new TextQuery(query ?? pattern, glob ?? path, workspace, maxResults, Regex: false, context, unique, root, exclude, matchesOnly, queries, countOnly, containers, word), cancellationToken);

    [McpServerTool(Name = "search_regex", ReadOnly = true)]
    [Description("Regular-expression search across the workspace, or across any absolute directory with root=. Pass queries to search up to 10 expressions in ONE pass over the same file set. Replaces one call per expression, and every record is tagged q1..qN by the position of its expression in queries=, which is what an alternation cannot do: it returns one undifferentiated list. A line matching several of them is ONE record carrying all of their tags, comma-separated in query order (q1,q3). An expression that spans a line break - a literal newline, [\\s\\S] or (?s). - is reported once, at the line its text starts on, and the scan resumes on the next line, so every other expression still sees the lines it spanned. The count line is how many matching LINES exist, at most one per line, and a zero result proves absence in the files it searched - bin, obj, .git, .claude, .vs, .idea, artifacts, TestResults, node_modules and directory symlinks are skipped. ^ and $ anchor each line, and a match that spans several lines is reported once, at the first line carrying its text. countOnly=true answers ONE line per file with its match count and no matched text, tagged q1=N per expression. context=N adds the surrounding lines so a hit needs no follow-up read, matchesOnly=true prints the matched span instead of the whole line the way grep -o does, unique=true collapses identical matching lines to one record with x<count>, and exclude= drops the paths a glob= cannot leave out. containers=true names the C# declaration each hit sits in, so a hit is an id get_symbol_source takes; word= belongs to search_text, because \\b answers it here. Results are tagged HEURISTIC.")]
    public Task<string> SearchRegex(
        [Description(".NET regular expression.")] string? query = null,
        [Description("Optional file glob, e.g. *.cs or **/Views/*.xaml. ** spans directories, * and ? stop at a separator.")] string? glob = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0,
        [Description("Lines of surrounding context per hit, 0-5. Default 0, which returns the matching line only.")] int context = 0,
        [Description("Collapse identical matching lines to one record carrying x<count>. Use on logs and generated output.")] bool unique = false,
        [Description("Absolute directory to search instead of the workspace, e.g. a log folder. The answer is tagged outside-workspace.")] string? root = null,
        [Description("Alias for query.")] string? pattern = null,
        [Description("Alias for glob.")] string? path = null,
        [Description("Glob of paths to drop after glob= has selected them, e.g. .research/** or **/*.generated.cs.")] string? exclude = null,
        [Description("Print the matched span instead of the whole line, the way grep -o does; compose with unique=true for the distinct values of a shape. Default false.")] bool matchesOnly = false,
        [Description("One line per file - path and its match count, q1=N per expression - and no matched text. Refused beside matchesOnly=, unique= and context=. Default false.")] bool countOnly = false,
        [Description("Pass queries to search several expressions in one pass over the same file set, at most 10. Replaces one call per expression; every record is tagged q1..qN by the position of its expression here, which an alternation cannot do. Combines with query, which is taken first.")] string?[]? queries = null,
        [Description("Name the C# declaration each hit sits in - Type.Member, from syntax - so the record is an id get_symbol_source takes. .cs only; refused beside countOnly=. Default false.")] bool containers = false,
        CancellationToken cancellationToken = default) =>
        Search(new TextQuery(query ?? pattern, glob ?? path, workspace, maxResults, Regex: true, context, unique, root, exclude, matchesOnly, queries, countOnly, containers), cancellationToken);

    private Task<string> Search(TextQuery request, CancellationToken cancellationToken)
    {
        if (Refusable(request) is { } refusal)
            return Task.FromResult(refusal.Render());

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
            request.MatchesOnly,
            request.CountOnly,
            request.Containers,
            request.Word,
            request.MaxResults is > 0 and <= NavigationTools.MaxCap);

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
        IReadOnlyList<string?>? Texts = null,
        bool CountOnly = false,
        bool Containers = false,
        bool Word = false);

    private Task<string> Guarded(
string? workspace,
string path,
Func<LoadedWorkspace, Task<string>> action,
bool? semantic = null,
CancellationToken cancellationToken = default) =>
context.RejectWrite() is { } rejection
    ? Task.FromResult(rejection)
    : context.WithWorkspaceAsync(workspace, path, action, semantic ?? SourceFile.IsCSharp(path), cancellationToken);

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
        string? name,
        int depth,
        bool chosen,
        CancellationToken cancellationToken)
    {
        if (!tracked)
            return TextSearchService.FindFiles(loaded, glob, maxResults, stamps, null, name, depth, chosen);

        var known = await TrackedAsync(loaded, cancellationToken).ConfigureAwait(false);

        return known.IsOk
            ? TextSearchService.FindFiles(loaded, glob, maxResults, stamps, known.Value!, name, depth, chosen)
            : known.Error!.Render();
    }

    private const int MaxBatchedEdits = 10;

    private static async Task<string> EditedAsync(
        LoadedWorkspace loaded,
        string path,
        FileService.EditRequest request,
        string? newText,
        FileService.TextEdit[]? edits,
        FileService.TextRow[]? rows,
        CancellationToken cancellationToken)
    {
        if ((request.Row is { Length: > 0 } || rows is { Length: > 0 }) && request.ToPath is not { Length: > 0 })
        {
            return Errors.Invalid(
                "row= and rows= address markdown table rows to MOVE, and no toPath= was passed",
                "pass toPath=<the other markdown file>, or edit the row in place with oldText=").Render();
        }

        if (request.ToPath is { Length: > 0 } moved)
            return await MovedAsync(loaded, path, moved, request, newText, edits, rows, cancellationToken).ConfigureAwait(false);

        if (edits is not { Length: > 0 } batch)
        {
            return newText is null
                ? Errors.Blank("newText", "edits").Render()
                : NavigationTools.Unwrap(await FileService.EditTextAsync(loaded, path, request, cancellationToken).ConfigureAwait(false));
        }

        if (newText is not null || request.OldText is { Length: > 0 } || request.Section is { Length: > 0 } || request.Place is { Length: > 0 })
        {
            return Errors.Invalid(
                "edits= was passed together with a top-level oldText, newText, section or place, and the top-level edit would have been silently dropped",
                "put every edit in edits=, or send the single edit without edits=").Render();
        }

        if (batch.Length > MaxBatchedFiles)
        {
            return Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"edits carried {batch.Length} entries, at most {MaxBatchedFiles} are applied in one call"),
                string.Create(CultureInfo.InvariantCulture, $"split it into smaller calls - at most {MaxBatchedEdits} per file and {MaxBatchedFiles} in total")).Render();
        }

        var grouped = Grouped(path, batch);

        return grouped.IsOk
            ? NavigationTools.Unwrap(await FileService.EditTextGroupedAsync(loaded, grouped.Value!, request, cancellationToken).ConfigureAwait(false))
            : grouped.Error!.Render();
    }

    private static async Task<string> MovedAsync(
    LoadedWorkspace loaded,
    string path,
    string toPath,
    FileService.EditRequest request,
    string? newText,
    FileService.TextEdit[]? edits,
    FileService.TextRow[]? rows,
    CancellationToken cancellationToken)
    {
        if (request.Row is { Length: > 0 } || rows is { Length: > 0 })
            return await RowMovedAsync(loaded, path, toPath, request, edits, rows, cancellationToken).ConfigureAwait(false);

        if (newText is not null || request.OldText is { Length: > 0 } || edits is { Length: > 0 })
        {
            return Errors.Invalid(
                "toPath moves a section and cannot be combined with newText, oldText or edits",
                "send the move as path=, section= and toPath= alone").Render();
        }

        if (request.Section is not { Length: > 0 })
        {
            return Errors.Invalid(
                "toPath moves a markdown section and no section= was passed",
                "pass section=\"## Open\" beside toPath=, or row=\"I286\" to move one table row, or use write_text to replace the whole file").Render();
        }

        return NavigationTools.Unwrap(
            await FileService.MoveSectionAsync(loaded, path, toPath, request, cancellationToken).ConfigureAwait(false));
    }

    private Task<string> ReadOneAsync(
        string path,
        FileService.ReadRequest request,
        bool whole,
        string? workspace,
        CancellationToken cancellationToken) =>
        whole && path.AsSpan().EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !context.OutsideEveryWorkspace(path)
            ? context.WithWorkspaceAsync(
                workspace,
                path,
                async loaded => NavigationTools.Unwrap(
                    await OutlineService.OrTextAsync(loaded, path, request, cancellationToken).ConfigureAwait(false)),
                cancellationToken: cancellationToken)
            : Read(path, request, workspace, cancellationToken);

    private async Task<string> ReadManyAsync(
    ImmutableArray<string> paths,
    FileService.ReadRequest request,
    bool whole,
    string? workspace,
    CancellationToken cancellationToken)
    {
        var rendered = new List<string>(paths.Length);
        var remaining = request.Range.Budget;
        var clipped = string.Empty;

        foreach (var path in paths)
        {
            var scoped = request with { Range = request.Range with { MaxChars = Math.Max(1, remaining) } };
            var answer = await ReadOneAsync(path, scoped, whole, workspace, cancellationToken).ConfigureAwait(false);

            rendered.Add(Entry(path, answer));
            remaining -= answer.Length;

            if (remaining > 0 || rendered.Count == paths.Length)
                continue;

            clipped = path;

            break;
        }

        return Batched(rendered, paths.Length, clipped);
    }

    private static string Entry(string path, string answer) => answer.StartsWith("ERROR", StringComparison.Ordinal)
    ? string.Create(CultureInfo.InvariantCulture, $"{(answer.Contains("DocumentNotFound", StringComparison.Ordinal) ? "NOT_FOUND" : "FAILED")} {path}\n{answer}")
    : string.Create(CultureInfo.InvariantCulture, $"{path}\n{answer}");

    private static string Batched(List<string> rendered, int requested, string clipped)
    {
        var response = new ResponseBuilder("read_text", string.Empty);

        response.Summary(rendered.Count, requested, "files", "maxChars=");

        foreach (var entry in rendered)
            response.Line(entry);

        if (clipped is { Length: > 0 })
        {
            response.Note(string.Create(
                CultureInfo.InvariantCulture,
                $"the shared maxChars budget ran out at {clipped} - raise maxChars, or read the rest in a second call"));
        }

        return response.ToString();
    }

    private const int MaxBatchedFiles = 25;

    private static Result<List<FileService.TextEditGroup>> Grouped(string path, FileService.TextEdit[] edits)
    {
        var order = new List<string>();
        var byPath = new Dictionary<string, List<FileService.TextEdit>>(StringComparer.OrdinalIgnoreCase);

        foreach (var edit in edits)
            Collect(byPath, order, edit.Path is { Length: > 0 } target ? target : path, edit);

        var oversized = order.Find(entry => byPath[entry].Count > MaxBatchedEdits);

        if (oversized is { Length: > 0 })
        {
            return Result.Fail<List<FileService.TextEditGroup>>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"edits carried {byPath[oversized].Count} entries for {oversized}, at most {MaxBatchedEdits} per file are applied as one write"),
                "split it into smaller calls - batched-item accuracy falls off past about six edits per file"));
        }

        List<FileService.TextEditGroup> groups = [.. order.Select(entry => new FileService.TextEditGroup(entry, byPath[entry]))];

        return Result.Ok(groups);
    }

    private static void Collect(
        Dictionary<string, List<FileService.TextEdit>> byPath,
        List<string> order,
        string target,
        FileService.TextEdit edit)
    {
        if (!byPath.TryGetValue(target, out var list))
        {
            list = [];
            byPath[target] = list;
            order.Add(target);
        }

        list.Add(edit);
    }

    private static bool TouchesCSharp(string path, FileService.TextEdit[]? edits) =>
    SourceFile.IsCSharp(path) || (edits ?? []).Any(edit => edit.Path is { Length: > 0 } target && SourceFile.IsCSharp(target));

    private const int MaxBatchedWrites = 10;

    private Task<string> WrittenOne(
        string? workspace,
        string? path,
        string? content,
        bool delete,
        string? reference,
        bool allowEmpty,
        WriteOptions options,
        CancellationToken cancellationToken)
    {
        if (path is not { Length: > 0 } target)
            return Task.FromResult(Errors.Blank("path", "files").Render());

        if (reference is { Length: > 0 } from)
            return Restored(workspace, target, from, allowEmpty, options, cancellationToken);

        return delete
            ? Guarded(workspace, target, async loaded => NavigationTools.Unwrap(
                await FileService.DeleteAsync(loaded, target, options.DryRun, options.Force, cancellationToken).ConfigureAwait(false)), cancellationToken: cancellationToken)
            : Written(workspace, target, content, allowEmpty, options, cancellationToken);
    }

    private Task<string> WrittenMany(
    string? workspace,
    string? path,
    string? content,
    bool delete,
    bool allowEmpty,
    FileService.FileWrite[] files,
    WriteOptions options,
    CancellationToken cancellationToken)
    {
        if (Refused(path, content, delete, allowEmpty, files) is { } refusal)
            return Task.FromResult(refusal.Render());

        return Guarded(
            workspace,
            files[0].Path,
            async loaded => NavigationTools.Unwrap(await FileService.WriteTextManyAsync(
                loaded, files, options.DryRun, options.Force, options.AllowErrors, options.Verbose, options.AllowPolicy, cancellationToken).ConfigureAwait(false)),
            files.Any(file => SourceFile.IsCSharp(file.Path)),
            cancellationToken);
    }

    private static TerseError? Refused(string? path, string? content, bool delete, bool allowEmpty, FileService.FileWrite[] files)
    {
        if (content is not null || delete || path is { Length: > 0 })
        {
            return Errors.Invalid(
                "files= was passed together with a top-level path, content or delete, and that write would have been silently dropped",
                "put every write in files=, or send the single write without files=");
        }

        if (files.Length > MaxBatchedWrites)
        {
            return Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"files carried {files.Length} entries, at most {MaxBatchedWrites} are written in one call"),
                string.Create(CultureInfo.InvariantCulture, $"send at most {MaxBatchedWrites} per call"));
        }

        var missing = Array.FindIndex(files, file => file.Path is not { Length: > 0 } || file.Content is null);

        if (missing >= 0)
        {
            return Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'files' entry {missing + 1} carries no {(files[missing].Path is { Length: > 0 } ? "content" : "path")}"),
                "every entry needs both a path and its full new content");
        }

        var empty = allowEmpty ? -1 : Array.FindIndex(files, file => file.Content.Length is 0);

        return empty < 0
            ? null
            : Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'files' entry {empty + 1} - {files[empty].Path} - is empty, which would truncate the file"),
                "pass the full new content; to truncate deliberately pass allowEmpty=true, to remove the file pass delete=true");
    }

    private static string[]? Columned(string? columns) =>
        columns is { Length: > 0 }
            ? [.. columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : null;

    private Task<string> Restored(
        string? workspace,
        string path,
        string reference,
        bool allowEmpty,
        WriteOptions options,
        CancellationToken cancellationToken) =>
        Guarded(workspace, path, async loaded => await RestoredAsync(loaded, path, reference, allowEmpty, options, cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken);

    private static async Task<string> RestoredAsync(
        LoadedWorkspace loaded,
        string path,
        string reference,
        bool allowEmpty,
        WriteOptions options,
        CancellationToken cancellationToken)
    {
        var relative = PositionFormat.Relative(loaded.Root, Path.IsPathRooted(path) ? path : Path.Combine(loaded.Root, path));
        var shown = await GitRunner.ShowAsync(loaded.Root, reference, relative, cancellationToken).ConfigureAwait(false);

        if (!shown.IsOk)
            return shown.Error!.Render();

        if (shown.Value is not { Length: > 0 } && !allowEmpty)
        {
            return Errors.Invalid(
                relative + " is empty at " + reference + ", so restoring it would truncate the file",
                "pass allowEmpty=true to restore it empty, or delete=true to remove it").Render();
        }

        return NavigationTools.Unwrap(await FileService.WriteTextAsync(
            loaded, path, shown.Value!, options.DryRun, options.Force, options.AllowErrors, options.Verbose, options.AllowPolicy, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<string> RowMovedAsync(
        LoadedWorkspace loaded,
        string path,
        string toPath,
        FileService.EditRequest request,
        FileService.TextEdit[]? edits,
        FileService.TextRow[]? rows,
        CancellationToken cancellationToken)
    {
        if (request.OldText is { Length: > 0 } || request.Section is { Length: > 0 } || edits is { Length: > 0 })
        {
            return Errors.Invalid(
                "row= and rows= move markdown table rows and cannot be combined with oldText, section or edits",
                "send the move as path=, row= or rows=, toPath= and optionally newText= alone").Render();
        }

        if (Batched(request, rows) is { IsOk: false } refused)
            return refused.Error!.Render();

        return NavigationTools.Unwrap(rows is { Length: > 0 } batch
            ? await FileService.MoveRowsAsync(loaded, path, toPath, batch, request, cancellationToken).ConfigureAwait(false)
            : await FileService.MoveRowAsync(loaded, path, toPath, request, cancellationToken).ConfigureAwait(false));
    }

    private static Result<bool> Batched(FileService.EditRequest request, FileService.TextRow[]? rows)
    {
        if (rows is not { Length: > 0 })
            return Result.Ok(true);

        if (request.Row is { Length: > 0 })
        {
            return Result.Fail<bool>(Errors.Invalid(
                "rows= and row= were both passed, and the single row would have been silently dropped",
                "put every row in rows=, or send the single move without rows="));
        }

        if (rows.Length > MaxBatchedFiles)
        {
            return Result.Fail<bool>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"rows carried {rows.Length} entries, at most {MaxBatchedFiles} are moved in one call"),
                string.Create(CultureInfo.InvariantCulture, $"send at most {MaxBatchedFiles} per call")));
        }

        var blank = Array.FindIndex(rows, entry => entry.Row is not { Length: > 0 });

        return blank < 0
            ? Result.Ok(true)
            : Result.Fail<bool>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'rows' entry {blank + 1} carries no row identifier"),
                "every entry needs the row's first cell, e.g. row=\"I286\""));
    }

    private TerseError? Foreign(string? workspace, string path)
    {
        if (!Path.IsPathRooted(path))
            return null;

        var full = Path.GetFullPath(path);
        var owner = context.Registry.All().FirstOrDefault(loaded => PathBoundary.Contains(loaded.Root, full));

        if (owner is null)
            return null;

        var resolved = context.Registry.Resolve(workspace, path, semantic: false);

        return resolved.IsOk && PathBoundary.Contains(resolved.Value!.Workspace.Root, full)
            ? null
            : Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{full}' is inside the loaded workspace at {owner.Root}, not the one this call resolved to"),
                "send the write with workspace=<that solution's name>, so it goes through that workspace's own compile gate rather than around it");
    }

    private static TerseError? Refusable(TextQuery request)
    {
        if (request.MatchesOnly && !request.Regex)
        {
            return Errors.Invalid(
                "'matchesOnly' prints the matched span, and a literal query's span is the query itself, so every record would read back the text you passed",
                "drop matchesOnly=, pass countOnly=true for one count per file, or use search_regex where the matched span varies");
        }

        if (request.CountOnly && (request.MatchesOnly || request.Unique || request.Context > 0))
        {
            return Errors.Invalid(
                "'countOnly' answers one line per file with no matched text, so matchesOnly=, unique= and context= have nothing to act on",
                "drop countOnly= to read the matching lines, or drop matchesOnly=, unique= and context=");
        }

        if (request.CountOnly && request.Containers)
        {
            return Errors.Invalid(
                "'countOnly' answers one line per file with no matched line, so containers= has no hit to name a declaration for",
                "drop countOnly= to read the matching lines with their declarations, or drop containers=");
        }

        return null;
    }

    private static TerseError? Refused(string? section, int occurrence, bool headings, int maxLevel)
    {
        if (occurrence > 0 && section is not { Length: > 0 })
        {
            return Errors.Invalid(
                "'occurrence' picks which section= to read, and no section was passed",
                "pass section=\"### Added\" beside it, or drop occurrence=");
        }

        if (maxLevel > 0 && !headings)
        {
            return Errors.Invalid(
                "'maxLevel' narrows the heading map, and headings=true was not passed",
                "pass headings=true beside it, or drop maxLevel=");
        }

        return null;
    }

    private Task<string> ManyAsync(
        ImmutableArray<string> globs,
        string? workspace,
        int maxResults,
        bool stamps,
        bool tracked,
        string? name,
        int depth,
        CancellationToken cancellationToken) =>
        context.WithWorkspaceAsync(
            workspace,
            null,
            loaded => ListedManyAsync(loaded, globs, NavigationTools.Cap(maxResults, 100), stamps, tracked, name, depth, maxResults > 0, cancellationToken),
            semantic: false,
            cancellationToken);

    private static async Task<string> ListedManyAsync(
        LoadedWorkspace loaded,
        IReadOnlyList<string> globs,
        int maxResults,
        bool stamps,
        bool tracked,
        string? name,
        int depth,
        bool chosen,
        CancellationToken cancellationToken)
    {
        if (!tracked)
            return TextSearchService.FindFilesMany(loaded, globs, maxResults, stamps, null, name, depth, chosen);

        var known = await TrackedAsync(loaded, cancellationToken).ConfigureAwait(false);

        return known.IsOk
            ? TextSearchService.FindFilesMany(loaded, globs, maxResults, stamps, known.Value!, name, depth, chosen)
            : known.Error!.Render();
    }
}
