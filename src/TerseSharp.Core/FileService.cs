using System.Buffers;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

public static class FileService
{
    public const int MaxResponseCharacters = 128 * 1024;

    public const int DefaultResponseCharacters = 40 * 1024;

    private const int MaxNearMisses = 3;

    public static async Task<Result<string>> ReadTextAsync(
    LoadedWorkspace workspace,
    string path,
    ReadRequest request,
    CancellationToken cancellationToken)
    {
        var resolved = Readable(workspace, path);

        if (!resolved.IsOk)
            return Result.Fail<string>(resolved.Error!);

        var full = resolved.Value!;
        var label = PathBoundary.Contains(workspace.Root, full) ? path : Outside(full);

        return File.Exists(full)
            ? await PresentFileAsync(full, label, request, cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(await MissingDocument.ReadAsync(workspace, label, cancellationToken).ConfigureAwait(false));
    }

    public static async Task<Result<string>> WriteTextAsync(
            LoadedWorkspace workspace,
            string path,
            string content,
            bool dryRun,
            bool force,
            bool allowErrors,
            bool verbose,
            bool allowPolicy,
            CancellationToken cancellationToken)
    {
        var resolved = Writable(workspace, path, force);

        if (!resolved.IsOk)
            return Result.Fail<string>(resolved.Error!);

        var full = resolved.Value!;

        if (SourceFile.Reject(path, full, force) is { } refusal)
            return refusal;

        return PathBoundary.Contains(workspace.Root, full)
            ? await InsideAsync(workspace, path, full, content, dryRun, allowErrors, verbose, allowPolicy, cancellationToken).ConfigureAwait(false)
            : await OutsideWriteAsync(workspace, full, content, dryRun, verbose, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Result<string>> EditTextAsync(
        LoadedWorkspace workspace,
        string path,
        EditRequest request,
        CancellationToken cancellationToken)
    {
        var resolved = PathGuard.Resolve(workspace, path);

        if (!resolved.IsOk)
            return Result.Fail<string>(resolved.Error!);

        var full = resolved.Value!;

        if (SourceFile.Reject(path, full, request.Force) is { } refusal)
            return refusal;

        if (!File.Exists(full))
            return Result.Fail<string>(Errors.DocumentNotFound(path));

        var before = await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);
        var notes = new List<string>();
        var rewritten = Rewrite(before, request, notes);

        return rewritten.IsOk
            ? await ApplyAsync(workspace, full, path, before, rewritten.Value!, request, notes, cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(rewritten.Error!);
    }

    private static Result<string> Rewrite(string before, EditRequest request, List<string> notes)
    {
        if (Misplaced(request) is { } misplaced)
            return Result.Fail<string>(misplaced);

        return request.Section is { Length: > 0 } section
            ? Section(before, section, request.NewText, request.Place, request.Occurrence)
            : Snippet(before, request.OldText, request.NewText, request.Occurrence, notes);
    }

    private static Result<string> Snippet(string before, string oldText, string newText, int occurrence, List<string> notes)
    {
        if (oldText.Length is 0)
            return Result.Fail<string>(Errors.Blank("oldText"));

        var match = SnippetSearch.Find(before, oldText, occurrence > 0 ? occurrence : 1);
        var selected = occurrence > 0 ? match.Start >= 0 : match.IsUnique;

        if (!selected)
            return Result.Fail<string>(NoMatch(before, oldText, match, occurrence));

        var ending = LineEndings.Dominant(before);
        var start = SplitCarriageReturn(before, match.Start) ? match.Start - 1 : match.Start;

        return Result.Ok(string.Concat(
            before.AsSpan(0, start),
            LineEndings.Adopt(Reindented(newText, match.Indent, notes), ending),
            before.AsSpan(match.Start + match.Length)));
    }

    private static Result<string> Section(string before, string heading, string newText, string? place, int occurrence)
    {
        var located = DocumentOutline.Locate(DocumentOutline.Headings(before), heading, occurrence);

        if (!located.IsOk)
            return Result.Fail<string>(located.Error!);

        var ending = LineEndings.Dominant(before);
        var lines = before.ReplaceLineEndings(ending).Split(ending);
        var (keep, resume) = Placed(lines, located.Value!, place);

        return Result.Ok(string.Join(ending, lines.Take(keep)
            .Concat(LineEndings.Adopt(newText, ending).Split(ending))
            .Concat(lines.Skip(resume))));
    }

    private static async Task<Result<string>> ApplyAsync(
        LoadedWorkspace workspace,
        string full,
        string path,
        string before,
        string after,
        EditRequest request,
        IReadOnlyList<string> notes,
        CancellationToken cancellationToken)
    {
        if (!request.DryRun)
            await WriteAsync(workspace, full, after, cancellationToken).ConfigureAwait(false);

        return Result.Ok(DiffResponse("edit_text", path, before, after, request.DryRun, request.Verbose, notes: notes));
    }

    private static TerseError NoMatch(string before, string oldText, SnippetMatch match, int occurrence)
    {
        if (occurrence > 0)
        {
            return Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"occurrence={occurrence} does not exist: oldText matched {match.Occurrences} times"),
                match.Occurrences is 0
                    ? "drop occurrence= and fix oldText first; " + Nearest(before, oldText)
                    : string.Create(CultureInfo.InvariantCulture, $"pass an occurrence between 1 and {match.Occurrences}"));
        }

        return match.Occurrences > 1
            ? Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"oldText matched {match.Occurrences} times, expected exactly 1"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"pass occurrence=1..{match.Occurrences} to pick one, include more surrounding text so the match is unique, or pass section= for a markdown heading{Candidates(before, oldText, match)}"))
            : Errors.Invalid(
                "oldText matched 0 times, expected exactly 1 (line endings and whitespace were already normalized before this verdict)",
                MatchesDedented(before, oldText)
                    ? "it matches once indentation and blank lines are ignored, so it was pasted from a dedented, blank-stripped payload such as get_symbol_source - address a .cs member with replace_symbol_body or replace_symbol, and re-read anything else with read_text verbose=true"
                    : Nearest(before, oldText));
    }

    private static string Candidates(string before, string oldText, SnippetMatch match)
    {
        var sites = SnippetSearch.Sites(before, oldText, MaxCandidateSites);

        return sites.Count is 0 || (sites.Count < match.Occurrences && sites.Count < MaxCandidateSites)
            ? string.Empty
            : "\n" + string.Join("\n", sites);
    }

    private const int MaxCandidateSites = 5;

    private static string Nearest(string before, string oldText) =>
        SnippetSearch.NearestRegion(before, oldText) is { Length: > 0 } region
            ? region
            : SnippetSearch.NearMisses(before, oldText, MaxNearMisses) is { Count: > 0 } hits
                ? "the file's closest lines are - copy one verbatim from read_text: " + string.Join(" | ", hits)
                : "no line of the file resembles the first line of oldText; re-read the file with read_text, or pass section= for a markdown heading";

    private static async Task WriteAsync(LoadedWorkspace workspace, string full, string content, CancellationToken cancellationToken)
    {
        await AtomicWrite.TextAsync(full, content, cancellationToken).ConfigureAwait(false);
        workspace.Sync.Notice(full);
        workspace.Indexes.Noticed(full);
    }

    private static string DiffResponse(
        string tool,
        string path,
        string before,
        string after,
        bool dryRun,
        bool verbose,
        string? outside = null,
        IReadOnlyList<string>? notes = null)
    {
        var response = new ResponseBuilder(tool, dryRun ? "dryRun" : "applied").Verbose(verbose);
        var escaped = EscapedMarkup(path, after);
        var noted = notes is { Count: > 0 };

        if (outside is { Length: > 0 })
            response.Note("outside-workspace  " + outside);

        foreach (var note in notes ?? [])
            response.Note(note);

        if (!dryRun && !verbose && !escaped && !noted && UnifiedDiff.ChangedLines(before, after) is var quick && quick > 0)
        {
            return response
                .Line(string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(path.AsSpan())}  changedLines={quick}"))
                .ToString();
        }

        var report = UnifiedDiff.Report(path, before, after);

        response.Summary(1, 1, "files changed");

        if (dryRun && !verbose)
            response.Note("dryRun");

        if (escaped)
            response.Note("WARNING the new content carries &lt; or &gt; and no raw '<' - markup written HTML-escaped is not markup; restore the file with write_text ref=HEAD if that was unintended");

        response.Line(report.Text);
        response.Line(string.Create(CultureInfo.InvariantCulture, $"changedLines={report.ChangedLines}"));

        return response.ToString();
    }

    private static Result<string> Present(string path, string label, string text, ReadRequest request)
    {
        var answer = Rendered(path, label, text, request);

        return answer.IsOk ? Result.Ok(answer.Value! + Stamps(request)) : answer;
    }

    private static LineRange Tailed(string text, ReadRequest request)
    {
        if (request.Tail <= 0)
            return request.Range;

        var total = CountLines(text);

        return request.Range with { Start = Math.Max(1, total - request.Tail + 1), End = 0 };
    }

    private static Result<string> Outline(string path, string label, string text, ReadRequest request)
    {
        if (!DocumentOutline.IsMarkdown(path))
        {
            return Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{label}' is not markdown, so it has no headings"),
                "drop headings=true, or use get_file_outline for a .cs file"));
        }

        var rows = HeadingRows(DocumentOutline.Headings(text), request.MaxLevel);
        var shown = Math.Min(rows.Count, request.Range.MaxLines);
        var response = new ResponseBuilder("read_text", label + " headings").Verbose(request.Verbose);

        response.Summary(shown, rows.Count, "sections", "maxLines= or maxLevel=");

        for (var index = 0; index < shown; index++)
            response.Line(rows[index]);

        return Result.Ok(response.ToString());
    }

    private static Result<string> Slice(string path, string text, string heading, ReadRequest request)
    {
        var located = DocumentOutline.Locate(DocumentOutline.Headings(text), heading, request.Occurrence);

        return located.IsOk
            ? Result.Ok(Render(
                path,
                text,
                request.Range with { Start = located.Value!.StartLine, End = located.Value!.EndLine },
                request))
            : Result.Fail<string>(located.Error!);
    }

    private static string Render(string path, string text, LineRange range, ReadRequest request)
    {
        var keepsBlanks = TextCompressor.KeepsBlankLines(Located(path)) || TextCompressor.HasMultilineLiteral(text);
        var selection = Collect(text, range, new ReadFormat(request.Verbose, keepsBlanks));
        var response = new ResponseBuilder("read_text", path).Verbose(request.Verbose);

        response.Summary(selection.CoveredLines, ReachableLines(selection), "lines");

        if (!request.Verbose && IsOutside(path))
            response.Note(OutsideMarker);

        AppendPastEnd(response, range, selection);

        foreach (var line in selection.Lines)
            response.Line(line);

        AppendContinuation(response, path, selection);
        AppendSections(response, path, text, request, selection);
        AppendMemberBatch(response, path, text, selection);

        return response.ToString();
    }
    private static void AppendContinuation(ResponseBuilder response, string path, LineSelection selection)
    {
        if (selection.CutLine is not 0)
        {
            response.Note(string.Create(
                CultureInfo.InvariantCulture,
                $"line {selection.CutLine} was cut mid-way to fit maxChars; raise maxChars to see the rest of it, because a line range cannot resume inside a line"));
        }

        if (selection.NextLine is 0)
            return;

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"next: startLine={selection.NextLine} (total={selection.TotalLines})"));

        if (SourceFile.IsCSharp(Located(path)))
            response.Note(string.Create(CultureInfo.InvariantCulture, $"outline: get_file_outline path={Located(path)}"));

        if (DocumentOutline.IsMarkdown(Located(path)))
        {
            response.Note(string.Create(
                CultureInfo.InvariantCulture,
                $"outline: read_text path={Located(path)} headings=true, then section=\"## Heading\" - paging costs one call per page, the heading map costs one"));
        }
    }

    private static ReadOnlySpan<char> Located(string label) => IsOutside(label)
        ? label.AsSpan(0, label.Length - OutsideSuffix.Length)
        : label;

    private static bool IsOutside(string label) => label.EndsWith(OutsideSuffix, StringComparison.Ordinal);

    private const string OutsideMarker = "outside-workspace";

    private static LineSelection Collect(string text, LineRange range, ReadFormat format)
    {
        var total = CountLines(text);
        var lines = new List<string>(Math.Min(range.MaxLines, 512));
        var budget = range.Budget;
        var number = 0;
        var previous = -1;
        var covered = 0;
        var last = 0;
        var shortened = 0;
        var clipped = false;

        foreach (var line in text.AsSpan().EnumerateLines())
        {
            number++;

            if (number > total)
                break;

            if (!range.Covers(number))
                continue;

            if (lines.Count >= range.MaxLines || budget <= 0)
            {
                clipped = true;

                break;
            }

            covered++;
            last = number;

            var emitted = format.Verbose ? line : line.TrimEnd();

            if (emitted.Length > budget)
                shortened = number;

            if (Dropped(line, format))
                continue;

            lines.Add(Emit(number, emitted, budget, format.Verbose || number != previous + 1));
            budget -= Math.Min(emitted.Length, budget);
            previous = number;
        }

        return new LineSelection(lines, total, covered, Continuation(clipped, last, total), shortened);
    }

    private static int Continuation(bool clipped, int last, int total) => (clipped, last) switch
    {
        (false, _) or (_, 0) => 0,
        _ when last >= total => 0,
        _ => last + 1,
    };

    private static bool Dropped(ReadOnlySpan<char> line, ReadFormat format) =>
        !format.Verbose && !format.KeepsBlankLines && line.TrimEnd().IsEmpty;

    private static string Emit(int number, ReadOnlySpan<char> line, int budget, bool numbered) => line.Length > budget || numbered
        ? Numbered(number, line, budget)
        : new string(line);

    private readonly record struct ReadFormat(bool Verbose, bool KeepsBlankLines);

    private static string Numbered(int number, ReadOnlySpan<char> line, int budget) => line.Length <= budget
        ? string.Create(CultureInfo.InvariantCulture, $"{number}: {line}")
        : string.Create(CultureInfo.InvariantCulture, $"{number}: {line[..budget]}... (+{line.Length - budget} chars)");

    public readonly record struct ReadRequest(LineRange Range, bool Headings, string? Section, bool Verbose = false, int Tail = 0, bool Bytes = false, long Length = 0, IReadOnlyList<string>? Columns = null, int Occurrence = 0, int MaxLevel = 0, bool Tokens = false, int Characters = 0);

    public readonly record struct EditRequest(string OldText, string NewText, string? Section, bool DryRun, bool Force, bool Verbose, int Occurrence = 0, string? Place = null, string? ToPath = null, string? Row = null);

    public readonly record struct LineRange(int Start, int End, int MaxLines, int MaxChars = DefaultResponseCharacters)
    {
        public int Budget => MaxChars > 0 ? MaxChars : DefaultResponseCharacters;

        public bool Covers(int line) => line >= Math.Max(1, Start) && line <= (End <= 0 ? int.MaxValue : End);
    }

    private readonly record struct LineSelection(
        IReadOnlyList<string> Lines,
        int TotalLines,
        int CoveredLines,
        int NextLine,
        int CutLine = 0);
    private static int CountLines(ReadOnlySpan<char> text)
    {
        if (text.Length is 0)
            return 0;

        var count = 1;
        var start = 0;

        while (text[start..].IndexOf('\n') is var offset and >= 0)
        {
            start += offset + 1;

            if (start >= text.Length)
                return count;

            count++;
        }

        return count;
    }
    private static async Task<Result<string>?> GatedAsync(
        LoadedWorkspace workspace,
        string path,
        string full,
        string content,
        bool dryRun,
        bool allowErrors,
        bool verbose,
        bool allowPolicy,
        CancellationToken cancellationToken)
    {
        if (!SourceFile.IsCSharp(path))
            return null;

        if (await StagedAsync(workspace, path, full, content, cancellationToken).ConfigureAwait(false) is not { } staged)
            return null;

        var options = new EditOptions("write_text", dryRun, allowErrors, verbose, AllowPolicy: allowPolicy);

        return await EditGate.ApplyAsync(workspace, staged.Updated, [staged.Id], options, cancellationToken).ConfigureAwait(false);
    }

    private static Result<string> Readable(LoadedWorkspace workspace, string path) =>
            Path.IsPathRooted(path) && !PathBoundary.Contains(workspace.Root, Path.GetFullPath(path))
                ? Result.Ok(Path.GetFullPath(path))
                : PathGuard.Resolve(workspace, path);

    private static string Unique(Dictionary<string, int> seen, string slug)
    {
        if (!seen.TryGetValue(slug, out var used))
        {
            seen[slug] = 0;

            return slug;
        }

        seen[slug] = used + 1;

        return string.Create(CultureInfo.InvariantCulture, $"{slug}-{used + 1}");
    }

    public static Task<Result<string>> ReadOutsideAsync(string path, ReadRequest request, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(path);

        return PresentFileAsync(full, Outside(full), request, cancellationToken);
    }

    private static string Outside(string full) => full + OutsideSuffix;

    private static async Task<Result<string>> PresentFileAsync(
            string full,
            string label,
            ReadRequest request,
            CancellationToken cancellationToken)
    {
        var file = new FileInfo(full);

        if (!file.Exists)
            return Result.Fail<string>(Errors.DocumentNotFound(label));

        if (BinaryContent.Reject(full, label) is { } binary)
            return binary;

        var text = await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);

        return Present(full, label, text, request with { Length = file.Length, Characters = text.Length });
    }

    private const string OutsideSuffix = "  " + OutsideMarker;

    public static async Task<Result<string>> DeleteAsync(
            LoadedWorkspace workspace,
            string path,
            bool dryRun,
            bool force,
            CancellationToken cancellationToken)
    {
        var resolved = PathGuard.Resolve(workspace, path);

        if (!resolved.IsOk)
            return Result.Fail<string>(resolved.Error!);

        var full = resolved.Value!;

        if (!File.Exists(full) && Directory.Exists(full))
            return RemovedDirectory(path, full, dryRun);

        if (SourceFile.Reject(path, full, force) is { } refusal)
            return refusal;

        if (!File.Exists(full))
            return Result.Fail<string>(Errors.DocumentNotFound(path));

        if (DocumentLookup.Find(workspace, path) is { } document)
            return await RemovedAsync(workspace, document, path, dryRun, cancellationToken).ConfigureAwait(false);

        if (!dryRun)
            File.Delete(full);

        return Result.Ok(Removed(path, dryRun));
    }

    private static async Task<Result<string>> RemovedAsync(
        LoadedWorkspace workspace,
        Microsoft.CodeAnalysis.Document document,
        string path,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (dryRun)
            return Result.Ok(Removed(path, true));

        var applied = await EditGate.ApplyAsync(
            workspace,
            workspace.Solution.RemoveDocument(document.Id),
            [document.Id],
            new EditOptions("write_text", false, true, false),
            cancellationToken).ConfigureAwait(false);

        if (!applied.IsOk)
            return applied;

        if (document.FilePath is { Length: > 0 } file && File.Exists(file))
            File.Delete(file);

        return Result.Ok(Removed(path, false));
    }

    private static string Removed(string path, bool dryRun) => string.Create(
        CultureInfo.InvariantCulture,
        $"write_text {(dryRun ? "dryRun" : "deleted")}  {path}");

    private static void AppendPastEnd(ResponseBuilder response, LineRange range, LineSelection selection)
    {
        if (selection.CoveredLines is not 0 || range.Start <= selection.TotalLines)
            return;

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"startLine={range.Start} is past the last line (total={selection.TotalLines})"));
    }

    private static string Dedented(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();
            if (!trimmed.IsEmpty)
                builder.Append(trimmed).Append('\n');
        }

        return builder.ToString();
    }

    private static bool MatchesDedented(string before, string oldText)
    {
        var needle = Dedented(oldText);

        return needle.Length is not 0 && Dedented(before).Contains(needle, StringComparison.Ordinal);
    }

    public readonly record struct TextEdit(string? OldText = null, string? NewText = null, string? Section = null, int Occurrence = 0, string? Path = null, string? Place = null);

    public readonly record struct TextEditGroup(string Path, IReadOnlyList<TextEdit> Edits);

    private static async Task<Result<(string Full, string Before)>> OpenedAsync(
        LoadedWorkspace workspace,
        string path,
        bool force,
        CancellationToken cancellationToken)
    {
        var resolved = PathGuard.Resolve(workspace, path);

        if (!resolved.IsOk)
            return Result.Fail<(string, string)>(resolved.Error!);

        var full = resolved.Value!;

        if (SourceFile.Reject(path, full, force) is { } refusal)
            return Result.Fail<(string, string)>(refusal.Error!);

        if (!File.Exists(full))
            return Result.Fail<(string, string)>(Errors.DocumentNotFound(path));

        return Result.Ok((full, await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false)));
    }

    private static string Failed(int number, TerseError error) => string.Create(
        CultureInfo.InvariantCulture,
        $"FAILED edit {number}  {error.Code}: {error.Message}\n  remedy: {error.Remedy}");

    private static EditRequest Requested(TextEdit edit, EditRequest request) => new(
        edit.OldText ?? string.Empty,
        edit.NewText ?? string.Empty,
        edit.Section,
        request.DryRun,
        request.Force,
        request.Verbose,
        edit.Occurrence,
        edit.Place);

    private static string BatchResponse(
        string path,
        string before,
        string after,
        List<string> failures,
        List<string> notes,
        int applied,
        int total,
        EditRequest request)
    {
        var response = new ResponseBuilder("edit_text", request.DryRun ? "dryRun" : "applied").Verbose(request.Verbose);
        var full = request.DryRun || request.Verbose;
        var report = full ? UnifiedDiff.Report(path, before, after) : new DiffReport(string.Empty, UnifiedDiff.ChangedLines(before, after));
        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path.AsSpan())}  changedLines={report.ChangedLines}  edits={applied}/{total}");

        if (!request.DryRun && !request.Verbose && failures.Count is 0 && notes.Count is 0)
            return response.Line(summary).ToString();

        response.Line(summary);

        if (request.DryRun)
            response.Note("dryRun");

        foreach (var note in notes)
            response.Note(note);

        foreach (var failure in failures)
            response.Note(failure);

        if (full)
            response.Line(report.Text);

        return response.ToString();
    }

    public static async Task<Result<string>> EditTextBatchAsync(
        LoadedWorkspace workspace,
        string path,
        IReadOnlyList<TextEdit> edits,
        EditRequest request,
        CancellationToken cancellationToken)
    {
        var opened = await OpenedAsync(workspace, path, request.Force, cancellationToken).ConfigureAwait(false);

        if (!opened.IsOk)
            return Result.Fail<string>(opened.Error!);

        var (full, before) = opened.Value;
        var failures = new List<string>();
        var notes = new List<string>();
        var after = before;
        var applied = 0;

        for (var index = 0; index < edits.Count; index++)
        {
            var rewritten = edits[index].NewText is null
                ? Result.Fail<string>(Errors.Blank("newText"))
                : Rewrite(after, Requested(edits[index], request), notes);

            if (rewritten.IsOk)
            {
                after = rewritten.Value!;
                applied++;
            }
            else
            {
                failures.Add(Failed(index + 1, rewritten.Error!));
            }
        }

        if (applied > 0 && !request.DryRun)
            await WriteAsync(workspace, full, after, cancellationToken).ConfigureAwait(false);

        return Result.Ok(BatchResponse(path, before, after, failures, notes, applied, edits.Count, request));
    }

    private static int ReachableLines(LineSelection selection) =>
        selection.NextLine is 0 ? selection.CoveredLines : selection.TotalLines;

    public static async Task<Result<string>> EditTextGroupedAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<TextEditGroup> groups,
        EditRequest request,
        CancellationToken cancellationToken)
    {
        var applied = new List<string>(groups.Count);
        var refused = new List<string>();
        var failures = new List<string>();

        foreach (var group in groups)
        {
            var answer = await EditTextBatchAsync(workspace, group.Path, group.Edits, request, cancellationToken).ConfigureAwait(false);

            Sort(answer, group.Path, applied, refused, failures);
        }

        return Result.Ok(applied.Count is 0
            ? string.Join('\n', failures)
            : string.Join('\n', applied.Concat(refused)));
    }

    public readonly record struct FileWrite(string Path, string Content);

    private readonly record struct PendingWrite(
        string Path,
        string Full,
        string Before,
        string After,
        Microsoft.CodeAnalysis.DocumentId? Document,
        bool IsNew);

    private static async Task<Result<PendingWrite>> PreparedAsync(
        LoadedWorkspace workspace,
        FileWrite file,
        bool force,
        CancellationToken cancellationToken)
    {
        var resolved = PathGuard.Resolve(workspace, file.Path);

        if (!resolved.IsOk)
            return Result.Fail<PendingWrite>(resolved.Error!);

        var full = resolved.Value!;

        if (SourceFile.Reject(file.Path, full, force) is { } refusal)
            return Result.Fail<PendingWrite>(refusal.Error!);

        var exists = File.Exists(full);
        var before = exists ? await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false) : string.Empty;
        var after = LineEndings.Adopt(file.Content, exists ? LineEndings.Dominant(before) : workspace.LineEnding);

        return Result.Ok(Pending(workspace, file.Path, full, before, after));
    }

    private static async Task<Result<string>?> GateManyAsync(
        LoadedWorkspace workspace,
        List<PendingWrite> pending,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var documents = pending.FindAll(entry => entry.Document is not null);

        if (documents.Count is 0)
            return null;

        var updated = workspace.Solution;
        var ids = new List<Microsoft.CodeAnalysis.DocumentId>(documents.Count);

        foreach (var entry in documents)
        {
            updated = await StagedIntoAsync(workspace, updated, entry, cancellationToken).ConfigureAwait(false);
            ids.Add(entry.Document!);
        }

        return await EditGate.ApplyAsync(workspace, updated, ids, options, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Result<string>> WriteTextManyAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<FileWrite> files,
        bool dryRun,
        bool force,
        bool allowErrors,
        bool verbose,
        bool allowPolicy,
        CancellationToken cancellationToken)
    {
        var pending = new List<PendingWrite>(files.Count);

        foreach (var file in files)
        {
            var prepared = await PreparedAsync(workspace, file, force, cancellationToken).ConfigureAwait(false);

            if (!prepared.IsOk)
                return Result.Fail<string>(prepared.Error!);

            pending.Add(prepared.Value);
        }

        var gated = await GateManyAsync(workspace, pending, new EditOptions("write_text", dryRun, allowErrors, verbose, AllowPolicy: allowPolicy), cancellationToken).ConfigureAwait(false);

        if (gated is { IsOk: false })
            return gated.Value;

        var rendered = new List<string>(pending.Count);

        if (gated is { } applied)
            rendered.Add(applied.Value!);

        foreach (var entry in pending)
            rendered.Add(await PlainAsync(workspace, entry, dryRun, verbose, cancellationToken).ConfigureAwait(false));

        return Result.Ok(string.Join('\n', rendered.FindAll(line => line.Length > 0)));
    }

    private static async Task<string> PlainAsync(
        LoadedWorkspace workspace,
        PendingWrite entry,
        bool dryRun,
        bool verbose,
        CancellationToken cancellationToken)
    {
        if (entry.Document is not null)
            return string.Empty;

        if (!dryRun)
            await WriteAsync(workspace, entry.Full, entry.After, cancellationToken).ConfigureAwait(false);

        return DiffResponse("write_text", entry.Path, entry.Before, entry.After, dryRun, verbose);
    }

    public static long? ByteLength(string? full) =>
            full is { Length: > 0 } && File.Exists(full) ? new FileInfo(full).Length : null;

    public static Result<string> Rendered(string path, string label, string text, ReadRequest request)
    {
        if (request.Columns is { Count: > 0 } columns)
            return Columned(path, label, text, request, columns);

        if (request.Headings)
            return Outline(path, label, text, request);

        return request.Section is { Length: > 0 } heading
            ? Slice(label, text, heading, request)
            : Result.Ok(Render(label, text, Tailed(text, request), request));
    }

    public static string Sized(long bytes) => string.Create(CultureInfo.InvariantCulture, $"bytes={bytes}");

    private static (int Keep, int Resume) Placed(string[] lines, DocumentSection section, string? place)
    {
        if (place is Prepend)
            return (section.StartLine, section.StartLine);

        if (place is not Append)
            return (section.StartLine - 1, section.EndLine);

        var last = section.EndLine;

        while (last > section.StartLine && string.IsNullOrWhiteSpace(lines[last - 1]))
            last--;

        return (last, last);
    }

    private static TerseError? Misplaced(EditRequest request) => (request.Place, request.Section) switch
    {
        (null or "", _) => null,
        (not (Append or Prepend), _) => Errors.Invalid(
            "place=" + request.Place + " is not a placement",
            "pass place=append or place=prepend, or omit place to replace the whole section"),
        (_, { Length: > 0 }) => null,
        _ => Errors.Invalid(
            "place was passed without a section, so there is no section to write inside",
            "pass the section too, e.g. section=\"## Commands\" place=append, or anchor the edit with oldText"),
    };

    private const string Append = "append";
    private const string Prepend = "prepend";

    private readonly record struct StagedWrite(
        Microsoft.CodeAnalysis.Solution Updated,
        Microsoft.CodeAnalysis.DocumentId Id);

    private static async Task<StagedWrite?> StagedAsync(
        LoadedWorkspace workspace,
        string path,
        string full,
        string content,
        CancellationToken cancellationToken)
    {
        if (DocumentLookup.Find(workspace, path) is not { } document)
            return Introduced(workspace, full, content);

        var existing = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var encoding = existing.Encoding ?? AtomicWrite.EncodingOf(document.FilePath!);
        var updated = workspace.Solution.WithDocumentText(document.Id, SourceText.From(content, encoding));

        return new StagedWrite(updated, document.Id);
    }

    private static StagedWrite? Introduced(LoadedWorkspace workspace, string full, string content)
    {
        if (CompilingProject(workspace, full) is not { } project)
            return null;

        var id = Microsoft.CodeAnalysis.DocumentId.CreateNewId(project.Id);
        var text = SourceText.From(content, AtomicWrite.EncodingOf(full));

        return new StagedWrite(DocumentPlacement.Add(workspace.Solution, id, full, text), id);
    }

    public static Microsoft.CodeAnalysis.Project? CompilingProject(LoadedWorkspace workspace, string full) =>
        workspace.Solution.Projects
            .Where(project => Compiles(project.FilePath, full))
            .OrderByDescending(project => Path.GetDirectoryName(project.FilePath)!.Length)
            .FirstOrDefault(project => ProjectGlobs.Memoized(project.FilePath!));

    private static bool Compiles(string? projectPath, string full) =>
        projectPath is { Length: > 0 }
        && Path.GetDirectoryName(projectPath) is { Length: > 0 } directory
        && PathBoundary.Contains(directory, full)
        && !WorkspaceFiles.IsExcluded(full, directory);

    private static async Task<Microsoft.CodeAnalysis.Solution> StagedIntoAsync(
        LoadedWorkspace workspace,
        Microsoft.CodeAnalysis.Solution updated,
        PendingWrite entry,
        CancellationToken cancellationToken)
    {
        if (entry.IsNew)
        {
            var added = SourceText.From(entry.After, AtomicWrite.EncodingOf(entry.Full));

            return DocumentPlacement.Add(updated, entry.Document!, entry.Full, added);
        }

        var document = workspace.Solution.GetDocument(entry.Document)!;
        var existing = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var encoding = existing.Encoding ?? AtomicWrite.EncodingOf(document.FilePath!);

        return updated.WithDocumentText(entry.Document!, SourceText.From(entry.After, encoding));
    }

    private static PendingWrite Pending(LoadedWorkspace workspace, string path, string full, string before, string after)
    {
        if (!SourceFile.IsCSharp(path))
            return new PendingWrite(path, full, before, after, null, false);

        if (DocumentLookup.Find(workspace, path) is { } document)
            return new PendingWrite(path, full, before, after, document.Id, false);

        return CompilingProject(workspace, full) is { } project
            ? new PendingWrite(path, full, before, after, Microsoft.CodeAnalysis.DocumentId.CreateNewId(project.Id), true)
            : new PendingWrite(path, full, before, after, null, false);
    }

    private static void AppendMemberBatch(ResponseBuilder response, string path, string text, LineSelection selection)
    {
        if (selection.NextLine is not 0 || selection.CoveredLines != selection.TotalLines || !SourceFile.IsCSharp(Located(path)))
            return;

        if (OutlineService.BatchFromText(new string(Located(path)), text) is { } batch)
            response.Note(batch);
    }

    private static bool EscapedMarkup(string path, string content) =>
        IsMarkup(path)
        && content.Contains("&lt;", StringComparison.Ordinal)
        && !content.Contains('<', StringComparison.Ordinal);

    private static bool IsMarkup(ReadOnlySpan<char> path) =>
        Path.GetExtension(path) is var extension
        && (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".props", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".targets", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".axaml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".resx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase));

    private static Result<string> Columned(string path, string label, string text, ReadRequest request, IReadOnlyList<string> columns)
    {
        if (Conflicting(path, label, request) is { } refusal)
            return Result.Fail<string>(refusal);

        var window = Windowed(text, request.Section, request.Occurrence);

        return window.IsOk
            ? MarkdownTable.Projected(label, text, columns, window.Value.Start, window.Value.End, request.Range.MaxLines, request.Section, request.Range.Budget)
            : Result.Fail<string>(window.Error!);
    }

    private static Result<LineRange> Windowed(string text, string? section, int occurrence)
    {
        if (section is not { Length: > 0 } heading)
            return Result.Ok(default(LineRange));

        var located = DocumentOutline.Locate(DocumentOutline.Headings(text), heading, occurrence);

        return located.IsOk
            ? Result.Ok(new LineRange(located.Value!.StartLine, located.Value!.EndLine, 0))
            : Result.Fail<LineRange>(located.Error!);
    }

    private static TerseError? Conflicting(string path, string label, ReadRequest request)
    {
        if (!DocumentOutline.IsMarkdown(path))
        {
            return Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{label}' is not markdown, so it has no table columns"),
                "drop columns=, or use get_file_outline for a .cs file");
        }

        if (request.Headings)
        {
            return Errors.Invalid(
                "headings=true and columns= narrow the same file two different ways",
                "pass headings=true for the section map, or columns= for the table rows, not both");
        }

        return request.Range.Start > 0 || request.Range.End > 0 || request.Tail > 0
            ? Errors.Invalid(
                "startLine=, endLine= and tail= do not bound a column projection, which is addressed by table rather than by line",
                "pass section= to scope the projection to one section's tables, or maxLines= to bound the rows")
            : null;
    }

    public static async Task<Result<string>> MoveSectionAsync(
        LoadedWorkspace workspace,
        string path,
        string toPath,
        EditRequest request,
        CancellationToken cancellationToken)
    {
        if (!DocumentOutline.IsMarkdown(path) || !DocumentOutline.IsMarkdown(toPath))
        {
            return Result.Fail<string>(Errors.Invalid(
                "toPath moves a markdown section, and one of the two paths is not markdown",
                "pass two markdown files, or move the text with read_text and write_text"));
        }

        var source = await OpenedAsync(workspace, path, force: false, cancellationToken).ConfigureAwait(false);

        if (!source.IsOk)
            return Result.Fail<string>(source.Error!);

        var destination = await OpenedAsync(workspace, toPath, force: false, cancellationToken).ConfigureAwait(false);

        if (!destination.IsOk)
            return Result.Fail<string>(destination.Error!);

        if (string.Equals(source.Value.Full, destination.Value.Full, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail<string>(Errors.Invalid(
                "toPath names the same file as path, so the move would cut the section and append it straight back",
                "pass a different file, or move the section inside one file with section= and place="));
        }

        var cut = Cut(source.Value.Before, request.Section!, request.Occurrence);

        if (!cut.IsOk)
            return Result.Fail<string>(cut.Error!);

        return await WriteTextManyAsync(
            workspace,
            [
                new FileWrite(path, cut.Value.Remainder),
                new FileWrite(toPath, Landed(destination.Value.Before, cut.Value.Section, request.Place)),
            ],
            request.DryRun,
            force: false,
            allowErrors: false,
            request.Verbose,
            allowPolicy: false,
            cancellationToken).ConfigureAwait(false);
    }

    private static Result<SectionCut> Cut(string before, string heading, int occurrence)
    {
        var located = DocumentOutline.Locate(DocumentOutline.Headings(before), heading, occurrence);

        if (!located.IsOk)
            return Result.Fail<SectionCut>(located.Error!);

        var ending = LineEndings.Dominant(before);
        var lines = before.ReplaceLineEndings(ending).Split(ending);
        var start = located.Value.StartLine - 1;
        var end = Math.Min(located.Value.EndLine, lines.Length);
        var remainder = string.Join(ending, lines.Take(start).Concat(lines.Skip(end))).TrimEnd();

        return Result.Ok(new SectionCut(
            string.Join(ending, lines.Skip(start).Take(end - start)).TrimEnd(),
            remainder.Length is 0 ? remainder : remainder + ending));
    }

    private static string Landed(string destination, string section, string? place)
    {
        var ending = LineEndings.Dominant(destination);
        var body = LineEndings.Adopt(section, ending);

        return string.Equals(place, Prepend, StringComparison.Ordinal)
            ? body + ending + ending + destination
            : destination.TrimEnd() + ending + ending + body + ending;
    }

    private readonly record struct SectionCut(string Section, string Remainder);

    private static readonly SearchValues<char> DelimiterCharacters = SearchValues.Create("|-: ");

    private static bool IsTableRow(ReadOnlySpan<char> line)
    {
        var span = line.Trim();

        return span.Length > 1 && span[0] is '|';
    }

    private static bool IsDelimiterRow(ReadOnlySpan<char> line) =>
        IsTableRow(line) && line.Trim().IndexOfAnyExcept(DelimiterCharacters) < 0;

    private static ReadOnlySpan<char> FirstCell(ReadOnlySpan<char> line)
    {
        var body = line.Trim()[1..];
        var end = body.IndexOf('|');

        return end < 0 ? body.Trim() : body[..end].Trim();
    }

    private static List<int> Matching(string[] lines, ReadOnlySpan<char> identifier)
    {
        var matched = new List<int>(2);

        for (var index = 0; index < lines.Length; index++)
        {
            if (IsTableRow(lines[index]) && !IsDelimiterRow(lines[index]) && FirstCell(lines[index]).Contains(identifier, StringComparison.Ordinal))
                matched.Add(index);
        }

        return matched;
    }

    private static int LastRowLine(string[] lines)
    {
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            if (IsTableRow(lines[index]))
                return index;
        }

        return -1;
    }

    private static TerseError NoRow(string[] lines, List<int> matched, string identifier) => matched.Count is 0
        ? Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"no markdown table row's first cell contains '{identifier}'"),
            "pass the identifier exactly as the table's first column spells it - read_text columns= lists them")
        : Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"'{identifier}' matches the first cell of {matched.Count} table rows: {Candidates(lines, matched)}"),
            Disambiguation(lines, matched, identifier));

    private static Result<RowCut> CutRow(string before, string identifier)
    {
        var ending = LineEndings.Dominant(before);
        var lines = before.ReplaceLineEndings(ending).Split(ending);
        var matched = Matching(lines, identifier);

        if (matched.Count is not 1)
            return Result.Fail<RowCut>(NoRow(lines, matched, identifier));

        var cut = matched[0];
        var remainder = string.Join(ending, lines.Where((_, index) => index != cut)).TrimEnd();

        return Result.Ok(new RowCut(lines[cut].TrimEnd(), remainder.Length is 0 ? remainder : remainder + ending));
    }

    private static Result<string> LandedRow(string destination, string row)
    {
        var ending = LineEndings.Dominant(destination);
        var lines = destination.ReplaceLineEndings(ending).Split(ending);
        var last = LastRowLine(lines);

        if (last < 0)
        {
            return Result.Fail<string>(Errors.Invalid(
                "toPath holds no markdown table to append the row to",
                "give the target file a table header and delimiter row first, then move the row into it"));
        }

        var landed = lines.Take(last + 1).Append(row.TrimEnd()).Concat(lines.Skip(last + 1));

        return Result.Ok(string.Join(ending, landed).TrimEnd() + ending);
    }

    private readonly record struct RowCut(string Row, string Remainder);

    private static async Task<Result<(string Source, string Destination)>> PairAsync(
        LoadedWorkspace workspace,
        string path,
        string toPath,
        string unit,
        CancellationToken cancellationToken)
    {
        if (!DocumentOutline.IsMarkdown(path) || !DocumentOutline.IsMarkdown(toPath))
        {
            return Result.Fail<(string, string)>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"toPath moves a markdown {unit}, and one of the two paths is not markdown"),
                "pass two markdown files, or move the text with read_text and write_text"));
        }

        var source = await OpenedAsync(workspace, path, force: false, cancellationToken).ConfigureAwait(false);

        if (!source.IsOk)
            return Result.Fail<(string, string)>(source.Error!);

        var destination = await OpenedAsync(workspace, toPath, force: false, cancellationToken).ConfigureAwait(false);

        if (!destination.IsOk)
            return Result.Fail<(string, string)>(destination.Error!);

        return string.Equals(source.Value.Full, destination.Value.Full, StringComparison.OrdinalIgnoreCase)
            ? Result.Fail<(string, string)>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"toPath names the same file as path, so the move would cut the {unit} and append it straight back"),
                "pass a different markdown file"))
            : Result.Ok((source.Value.Before, destination.Value.Before));
    }

    public static Task<Result<string>> MoveRowAsync(
        LoadedWorkspace workspace,
        string path,
        string toPath,
        EditRequest request,
        CancellationToken cancellationToken) =>
        MoveRowsAsync(workspace, path, toPath, [new TextRow(request.Row!, request.NewText)], request, cancellationToken);

    private static Result<string> Writable(LoadedWorkspace workspace, string path, bool force)
    {
        if (!Path.IsPathRooted(path))
            return PathGuard.Resolve(workspace, path);

        var full = Path.GetFullPath(path);

        if (PathBoundary.Contains(workspace.Root, full))
            return Result.Ok(full);

        return force ? Result.Ok(full) : Result.Fail<string>(Errors.OutsideWrite(full));
    }

    private static async Task<(string Before, string After)> AdoptedAsync(
            LoadedWorkspace workspace,
            string full,
            string content,
            CancellationToken cancellationToken)
    {
        var exists = File.Exists(full);
        var before = exists ? await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false) : string.Empty;

        return (before, LineEndings.Adopt(content, exists ? LineEndings.Dominant(before) : workspace.LineEnding));
    }

    private static async Task<Result<string>> InsideAsync(
            LoadedWorkspace workspace,
            string path,
            string full,
            string content,
            bool dryRun,
            bool allowErrors,
            bool verbose,
            bool allowPolicy,
            CancellationToken cancellationToken)
    {
        var (before, after) = await AdoptedAsync(workspace, full, content, cancellationToken).ConfigureAwait(false);

        if (await GatedAsync(workspace, path, full, after, dryRun, allowErrors, verbose, allowPolicy, cancellationToken).ConfigureAwait(false) is { } gated)
            return gated;

        if (!dryRun)
            await WriteAsync(workspace, full, after, cancellationToken).ConfigureAwait(false);

        return Result.Ok(DiffResponse("write_text", path, before, after, dryRun, verbose));
    }

    private static async Task<Result<string>> OutsideWriteAsync(
            LoadedWorkspace workspace,
            string full,
            string content,
            bool dryRun,
            bool verbose,
            CancellationToken cancellationToken)
    {
        var (before, after) = await AdoptedAsync(workspace, full, content, cancellationToken).ConfigureAwait(false);

        if (!dryRun)
            await AtomicWrite.TextAsync(full, after, cancellationToken).ConfigureAwait(false);

        return Result.Ok(DiffResponse("write_text", full, before, after, dryRun, verbose, full));
    }

    private static void AppendSections(ResponseBuilder response, string path, string text, ReadRequest request, LineSelection selection)
    {
        if (selection.NextLine is not 0 || !Whole(request) || !DocumentOutline.IsMarkdown(Located(path)))
            return;

        var sections = DocumentOutline.Headings(text);

        if (sections.Count is 0)
            return;

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"sections={sections.Count} - address one with read_text or edit_text section=\"{sections[0].Title}\" instead of an oldText anchor{Named(sections)}"));
    }

    private static bool Whole(ReadRequest request) =>
            !request.Headings
            && request.Section is not { Length: > 0 }
            && request.Columns is not { Count: > 0 }
            && request.Tail is 0
            && request.Range.Start <= 0
            && request.Range.End <= 0;

    private static string Named(IReadOnlyList<DocumentSection> sections)
    {
        if (sections.Count is 1)
            return string.Empty;

        var text = new StringBuilder(": ");

        for (var index = 1; index < Math.Min(sections.Count, MaxNamedSections); index++)
            text.Append(index is 1 ? string.Empty : " | ").Append(sections[index].Title);

        return sections.Count > MaxNamedSections ? text.Append(" | ...").ToString() : text.ToString();
    }

    private const int MaxNamedSections = 6;

    public readonly record struct TextRow(string? Row = null, string? NewText = null);

    public static async Task<Result<string>> MoveRowsAsync(
        LoadedWorkspace workspace,
        string path,
        string toPath,
        IReadOnlyList<TextRow> rows,
        EditRequest request,
        CancellationToken cancellationToken)
    {
        var pair = await PairAsync(workspace, path, toPath, "table row", cancellationToken).ConfigureAwait(false);

        if (!pair.IsOk)
            return Result.Fail<string>(pair.Error!);

        var moved = Fold(pair.Value.Source, pair.Value.Destination, rows);

        return moved.IsOk
            ? await WriteTextManyAsync(
                workspace,
                [new FileWrite(path, moved.Value.Source), new FileWrite(toPath, moved.Value.Destination)],
                request.DryRun,
                force: false,
                allowErrors: false,
                request.Verbose,
                allowPolicy: false,
                cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(moved.Error!);
    }

    private static Result<(string Source, string Destination)> Fold(string source, string destination, IReadOnlyList<TextRow> rows)
    {
        var carried = (Source: source, Destination: destination);

        foreach (var row in rows)
        {
            var moved = Moved(carried.Source, carried.Destination, row);

            if (!moved.IsOk)
                return moved;

            carried = moved.Value;
        }

        return Result.Ok(carried);
    }

    private static Result<(string Source, string Destination)> Moved(string source, string destination, TextRow row)
    {
        var cut = CutRow(source, row.Row ?? string.Empty);

        if (!cut.IsOk)
            return Result.Fail<(string, string)>(cut.Error!);

        var landed = LandedRow(destination, row.NewText is { Length: > 0 } ? row.NewText : cut.Value.Row);

        return landed.IsOk
            ? Result.Ok((cut.Value.Remainder, landed.Value!))
            : Result.Fail<(string, string)>(landed.Error!);
    }

    private static List<string> HeadingRows(IReadOnlyList<DocumentSection> sections, int maxLevel)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var rows = new List<string>(sections.Count);

        foreach (var section in sections)
        {
            var anchor = Unique(seen, MarkdownAnchor.Of(section.Heading));

            if (maxLevel is 0 || section.Level <= maxLevel)
            {
                rows.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{section.StartLine}-{section.EndLine}  {section.Heading}  #{anchor}"));
            }
        }

        return rows;
    }

    private static void Sort(Result<string> answer, string path, List<string> applied, List<string> refused, List<string> failures)
    {
        if (answer.IsOk)
        {
            applied.Add(answer.Value!);

            return;
        }

        var error = answer.Error!;

        failures.Add(error.Render());
        refused.Add(string.Create(CultureInfo.InvariantCulture, $"REFUSED {path}: {error.Code} - {error.Message}; remedy: {error.Remedy}"));
    }

    private static bool SplitCarriageReturn(ReadOnlySpan<char> before, int start) =>
        start > 0 && before[start] is '\n' && before[start - 1] is '\r';

    public static string Stamps(ReadRequest request) => (request.Bytes ? "\n" + Sized(request.Length) : string.Empty)
            + (request.Tokens ? "\n" + Counted(request.Characters) : string.Empty);

    public static string Counted(int characters) => string.Create(CultureInfo.InvariantCulture, $"tokens={(characters + 3) / 4}");

    public static async Task<int?> CharacterLengthAsync(string? full, CancellationToken cancellationToken)
    {
        if (full is not { Length: > 0 } || !File.Exists(full))
            return null;

        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, CountBufferChars, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);

        return await CountedAsync(reader, cancellationToken).ConfigureAwait(false);
    }

    private const int CountBufferChars = 8192;

    private static async Task<int> CountedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<char>.Shared.Rent(CountBufferChars);

        try
        {
            var characters = 0;
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);

            while (read > 0)
            {
                characters += read;
                read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            return characters;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static bool Carries(string text, string indent)
    {
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            if (!line.IsWhiteSpace() && !line.StartsWith(indent, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static string Indented(string text, string indent)
    {
        var builder = new StringBuilder(text.Length + (indent.Length * 4));
        var first = true;

        foreach (var line in text.AsSpan().EnumerateLines())
        {
            if (!first)
                builder.Append('\n');

            first = false;

            if (!line.IsWhiteSpace())
                builder.Append(indent);

            builder.Append(line);
        }

        return builder.ToString();
    }

    private static string Reindented(string newText, string? indent, List<string> notes)
    {
        if (indent is not { Length: > 0 } || Carries(newText, indent))
            return newText;

        notes.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"NOTE oldText matched after re-indenting by {indent.Length} column(s) - it was pasted from a dedented payload such as get_symbol_source, and newText was re-indented to match the file"));

        return Indented(newText, indent);
    }

    private const int MaxDirectoryEntries = 4;

    private static Result<string> RemovedDirectory(string path, string full, bool dryRun)
    {
        var held = new List<string>(MaxDirectoryEntries + 1);

        foreach (var entry in Directory.EnumerateFileSystemEntries(full))
        {
            held.Add(new string(Path.GetFileName(entry.AsSpan())));

            if (held.Count > MaxDirectoryEntries)
                break;
        }

        if (held.Count > 0)
        {
            return Result.Fail<string>(Errors.Invalid(
                "'" + path + "' is a directory and it is not empty: " + Listed(held),
                "only an EMPTY directory is removed - delete the files inside it first with write_text delete=true; no tool here removes more than it was pointed at"));
        }

        if (!dryRun)
            Directory.Delete(full);

        return Result.Ok("write_text " + (dryRun ? "dryRun" : "deleted") + "  " + path + "  directory");
    }

    private static string Listed(List<string> held) => held.Count > MaxDirectoryEntries
        ? string.Join(", ", held.GetRange(0, MaxDirectoryEntries)) + " and more it did not list"
        : string.Join(", ", held);

    private const int CandidateCap = 5;
    private const int PreviewLength = 40;

    private static string Candidates(string[] lines, List<int> matched)
    {
        var builder = new StringBuilder();

        foreach (var index in matched.Take(CandidateCap))
        {
            if (builder.Length > 0)
                builder.Append("; ");

            builder.Append(CultureInfo.InvariantCulture, $"line {index + 1}: {Preview(FirstCell(lines[index]))}");
        }

        if (matched.Count > CandidateCap)
            builder.Append(CultureInfo.InvariantCulture, $"; and {matched.Count - CandidateCap} more");

        return builder.ToString();
    }

    private static string Preview(ReadOnlySpan<char> cell) =>
        cell.Length <= PreviewLength ? cell.ToString() : string.Concat(cell[..PreviewLength], "...");

    private static string Disambiguation(string[] lines, List<int> matched, string identifier)
    {
        var longer = Unique(lines, matched, identifier);

        return longer is null
            ? "pass a longer identifier - a row is addressed by a value unique to its first column"
            : string.Create(CultureInfo.InvariantCulture, $"pass row=\"{longer}\", which matches only one of them - a row is addressed by a value unique to its first column");
    }

    private static string? Unique(string[] lines, List<int> matched, string identifier)
    {
        foreach (var index in matched.Take(CandidateCap))
        {
            var token = Token(FirstCell(lines[index]), identifier);

            if (token.Length > identifier.Length && Counted(lines, token) is 1)
                return token.ToString();
        }

        return null;
    }

    private static int Counted(string[] lines, ReadOnlySpan<char> identifier)
    {
        var count = 0;

        foreach (var line in lines)
        {
            if (IsTableRow(line) && !IsDelimiterRow(line) && FirstCell(line).Contains(identifier, StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    private static ReadOnlySpan<char> Token(ReadOnlySpan<char> cell, ReadOnlySpan<char> identifier)
    {
        var at = cell.IndexOf(identifier, StringComparison.Ordinal);

        if (at < 0)
            return default;

        var start = at;

        while (start > 0 && !char.IsWhiteSpace(cell[start - 1]))
            start--;

        var end = at + identifier.Length;

        while (end < cell.Length && !char.IsWhiteSpace(cell[end]))
            end++;

        return cell[start..end];
    }
}
