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
        CancellationToken cancellationToken)
    {
        var resolved = PathGuard.Resolve(workspace, path);

        if (!resolved.IsOk)
            return Result.Fail<string>(resolved.Error!);

        var full = resolved.Value!;

        if (SourceFile.Reject(path, full, force) is { } refusal)
            return refusal;

        var exists = File.Exists(full);
        var before = exists ? await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false) : string.Empty;
        var after = LineEndings.Adopt(content, exists ? LineEndings.Dominant(before) : workspace.LineEnding);

        if (await GatedAsync(workspace, path, after, dryRun, allowErrors, verbose, cancellationToken).ConfigureAwait(false) is { } gated)
            return gated;

        if (!dryRun)
            await WriteAsync(workspace, full, after, cancellationToken).ConfigureAwait(false);

        return Result.Ok(DiffResponse("write_text", path, before, after, dryRun, verbose));
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
        var rewritten = Rewrite(before, request);

        return rewritten.IsOk
            ? await ApplyAsync(workspace, full, path, before, rewritten.Value!, request, cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(rewritten.Error!);
    }

    private static Result<string> Rewrite(string before, EditRequest request) => request.Section is { Length: > 0 } section
        ? Section(before, section, request.NewText)
        : Snippet(before, request.OldText, request.NewText, request.Occurrence);

    private static Result<string> Snippet(string before, string oldText, string newText, int occurrence)
    {
        if (oldText.Length is 0)
            return Result.Fail<string>(Errors.Blank("oldText"));

        var match = SnippetSearch.Find(before, oldText, occurrence > 0 ? occurrence : 1);
        var selected = occurrence > 0 ? match.Start >= 0 : match.IsUnique;

        if (!selected)
            return Result.Fail<string>(NoMatch(before, oldText, match, occurrence));

        var ending = LineEndings.Dominant(before);

        return Result.Ok(string.Concat(
            before.AsSpan(0, match.Start),
            LineEndings.Adopt(newText, ending),
            before.AsSpan(match.Start + match.Length)));
    }

    private static Result<string> Section(string before, string heading, string newText)
    {
        var located = DocumentOutline.Locate(DocumentOutline.Headings(before), heading);

        if (!located.IsOk)
            return Result.Fail<string>(located.Error!);

        var ending = LineEndings.Dominant(before);
        var lines = before.ReplaceLineEndings(ending).Split(ending);
        var section = located.Value!;
        var tail = lines.Skip(section.EndLine);

        return Result.Ok(string.Join(ending, lines.Take(section.StartLine - 1)
            .Concat(LineEndings.Adopt(newText, ending).Split(ending))
            .Concat(tail)));
    }

    private static async Task<Result<string>> ApplyAsync(
        LoadedWorkspace workspace,
        string full,
        string path,
        string before,
        string after,
        EditRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.DryRun)
            await WriteAsync(workspace, full, after, cancellationToken).ConfigureAwait(false);

        return Result.Ok(DiffResponse("edit_text", path, before, after, request.DryRun, request.Verbose));
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
        SnippetSearch.NearMisses(before, oldText, MaxNearMisses) is { Count: > 0 } hits
            ? "the file's closest lines are - copy one verbatim from read_text: " + string.Join(" | ", hits)
            : "no line of the file resembles the first line of oldText; re-read the file with read_text, or pass section= for a markdown heading";

    private static async Task WriteAsync(LoadedWorkspace workspace, string full, string content, CancellationToken cancellationToken)
    {
        await AtomicWrite.TextAsync(full, content, cancellationToken).ConfigureAwait(false);
        workspace.Sync.Notice(full);
        workspace.Indexes.Noticed(full);
    }

    private static string DiffResponse(string tool, string path, string before, string after, bool dryRun, bool verbose)
    {
        var response = new ResponseBuilder(tool, dryRun ? "dryRun" : "applied").Verbose(verbose);

        if (!dryRun && !verbose && UnifiedDiff.ChangedLines(before, after) is var quick && quick > 0)
        {
            return response
                .Line(string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(path.AsSpan())}  changedLines={quick}"))
                .ToString();
        }

        var report = UnifiedDiff.Report(path, before, after);

        response.Summary(1, 1, "files changed");

        if (dryRun && !verbose)
            response.Note("dryRun");

        response.Line(report.Text);
        response.Line(string.Create(CultureInfo.InvariantCulture, $"changedLines={report.ChangedLines}"));

        return response.ToString();
    }

    private static Result<string> Present(string path, string label, string text, ReadRequest request)
    {
        var answer = Rendered(path, label, text, request);

        return request.Bytes && answer.IsOk
            ? Result.Ok(answer.Value! + "\n" + Sized(request.Length))
            : answer;
    }

    private static LineRange Tailed(string text, ReadRequest request)
    {
        if (request.Tail <= 0)
            return request.Range;

        var total = CountLines(text);

        return request.Range with { Start = Math.Max(1, total - request.Tail + 1), End = 0 };
    }

    private static Result<string> Outline(string path, string label, string text, bool verbose)
    {
        if (!DocumentOutline.IsMarkdown(path))
        {
            return Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{label}' is not markdown, so it has no headings"),
                "drop headings=true, or use get_file_outline for a .cs file"));
        }

        var sections = DocumentOutline.Headings(text);
        var response = new ResponseBuilder("read_text", label + " headings").Verbose(verbose);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        response.Summary(sections.Count, sections.Count, "sections");

        foreach (var section in sections)
        {
            response.Line(string.Create(
                CultureInfo.InvariantCulture,
                $"{section.StartLine}-{section.EndLine}  {section.Heading}  #{Unique(seen, MarkdownAnchor.Of(section.Heading))}"));
        }

        return Result.Ok(response.ToString());
    }

    private static Result<string> Slice(string path, string text, string heading, ReadRequest request)
    {
        var located = DocumentOutline.Locate(DocumentOutline.Headings(text), heading);

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

    public readonly record struct ReadRequest(LineRange Range, bool Headings, string? Section, bool Verbose = false, int Tail = 0, bool Bytes = false, long Length = 0);

    public readonly record struct EditRequest(string OldText, string NewText, string? Section, bool DryRun, bool Force, bool Verbose, int Occurrence = 0);

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
        string content,
        bool dryRun,
        bool allowErrors,
        bool verbose,
        CancellationToken cancellationToken)
    {
        if (!SourceFile.IsCSharp(path) || DocumentLookup.Find(workspace, path) is not { } document)
            return null;

        var existing = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var encoding = existing.Encoding ?? AtomicWrite.EncodingOf(document.FilePath!);
        var updated = workspace.Solution.WithDocumentText(document.Id, SourceText.From(content, encoding));
        var options = new EditOptions("write_text", dryRun, allowErrors, verbose);

        return await EditGate.ApplyAsync(workspace, updated, [document.Id], options, cancellationToken).ConfigureAwait(false);
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

        return Present(full, label, text, request.Bytes ? request with { Length = file.Length } : request);
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

    public readonly record struct TextEdit(string? OldText = null, string? NewText = null, string? Section = null, int Occurrence = 0, string? Path = null);

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
        edit.Occurrence);

    private static string BatchResponse(
        string path,
        string before,
        string after,
        List<string> failures,
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

        if (!request.DryRun && !request.Verbose && failures.Count is 0)
            return response.Line(summary).ToString();

        response.Line(summary);

        if (request.DryRun)
            response.Note("dryRun");

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
        var after = before;
        var applied = 0;

        for (var index = 0; index < edits.Count; index++)
        {
            var rewritten = edits[index].NewText is null
                ? Result.Fail<string>(Errors.Blank("newText"))
                : Rewrite(after, Requested(edits[index], request));

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

        return Result.Ok(BatchResponse(path, before, after, failures, applied, edits.Count, request));
    }

    private static int ReachableLines(LineSelection selection) =>
        selection.NextLine is 0 ? selection.CoveredLines : selection.TotalLines;

    public static async Task<Result<string>> EditTextGroupedAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<TextEditGroup> groups,
        EditRequest request,
        CancellationToken cancellationToken)
    {
        var rendered = new List<string>(groups.Count);

        foreach (var group in groups)
        {
            var answer = await EditTextBatchAsync(workspace, group.Path, group.Edits, request, cancellationToken).ConfigureAwait(false);

            rendered.Add(answer.IsOk ? answer.Value! : answer.Error!.Render());
        }

        return Result.Ok(string.Join('\n', rendered));
    }

    public readonly record struct FileWrite(string Path, string Content);

    private readonly record struct PendingWrite(
        string Path,
        string Full,
        string Before,
        string After,
        Microsoft.CodeAnalysis.DocumentId? Document);

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
        var document = SourceFile.IsCSharp(file.Path) ? DocumentLookup.Find(workspace, file.Path) : null;

        return Result.Ok(new PendingWrite(file.Path, full, before, after, document?.Id));
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
            var document = workspace.Solution.GetDocument(entry.Document)!;
            var existing = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var encoding = existing.Encoding ?? AtomicWrite.EncodingOf(document.FilePath!);

            updated = updated.WithDocumentText(entry.Document!, SourceText.From(entry.After, encoding));
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

        var gated = await GateManyAsync(workspace, pending, new EditOptions("write_text", dryRun, allowErrors, verbose), cancellationToken).ConfigureAwait(false);

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

    private static Result<string> Rendered(string path, string label, string text, ReadRequest request)
    {
        if (request.Headings)
            return Outline(path, label, text, request.Verbose);

        return request.Section is { Length: > 0 } heading
            ? Slice(label, text, heading, request)
            : Result.Ok(Render(label, text, Tailed(text, request), request));
    }

    public static string Sized(long bytes) => string.Create(CultureInfo.InvariantCulture, $"bytes={bytes}");
}
