using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

public static class FileService
{
    private const int MaxResponseCharacters = 128 * 1024;

    private const int MaxNearMisses = 3;

    public static Task<Result<string>> ReadTextAsync(
        LoadedWorkspace workspace,
        string path,
        ReadRequest request,
        CancellationToken cancellationToken)
    {
        var resolved = Readable(workspace, path);

        if (!resolved.IsOk)
            return Task.FromResult(Result.Fail<string>(resolved.Error!));

        var full = resolved.Value!;
        var label = PathBoundary.Contains(workspace.Root, full) ? path : Outside(full);

        return PresentFileAsync(full, label, request, cancellationToken);
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
        : Snippet(before, request.OldText, request.NewText);

    private static Result<string> Snippet(string before, string oldText, string newText)
    {
        if (oldText.Length is 0)
            return Result.Fail<string>(Errors.Blank("oldText"));

        var match = SnippetSearch.Find(before, oldText);

        if (!match.IsUnique)
            return Result.Fail<string>(NoMatch(before, oldText, match));

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

    private static TerseError NoMatch(string before, string oldText, SnippetMatch match) => match.Occurrences > 1
        ? Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"oldText matched {match.Occurrences} times, expected exactly 1"),
            "include more surrounding text so the match is unique, or pass section= for a markdown heading")
        : Errors.Invalid(
            "oldText matched 0 times, expected exactly 1 (line endings and whitespace were already normalized before this verdict)",
            Nearest(before, oldText));

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
        var response = new ResponseBuilder(tool, dryRun ? "dryRun" : "applied");
        var changed = UnifiedDiff.ChangedLines(before, after);

        response.Summary(1, 1, "files changed");

        if (!dryRun && !verbose)
            return response.Line(string.Create(CultureInfo.InvariantCulture, $"{path}  changedLines={changed}")).Note("(verbose=true for the diff)").ToString();

        response.Line(UnifiedDiff.Between(path, before, after));
        response.Line(string.Create(CultureInfo.InvariantCulture, $"changedLines={changed}"));

        return response.ToString();
    }

    private static Result<string> Present(string path, string label, string text, ReadRequest request)
    {
        if (request.Headings)
            return Outline(path, label, text);

        return request.Section is { Length: > 0 } heading
            ? Slice(label, text, heading, request)
            : Result.Ok(Render(label, text, request.Range));
    }

    private static Result<string> Outline(string path, string label, string text)
    {
        if (!DocumentOutline.IsMarkdown(path))
        {
            return Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{label}' is not markdown, so it has no headings"),
                "drop headings=true, or use get_file_outline for a .cs file"));
        }

        var sections = DocumentOutline.Headings(text);
        var response = new ResponseBuilder("read_text", label + " headings");
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
            ? Result.Ok(Render(path, text, new LineRange(located.Value!.StartLine, located.Value!.EndLine, request.Range.MaxLines)))
            : Result.Fail<string>(located.Error!);
    }

    private static string Render(string path, string text, LineRange range)
    {
        var selection = Collect(text, range);
        var response = new ResponseBuilder("read_text", path);

        response.Summary(selection.Lines.Count, selection.TotalLines, "lines");

        foreach (var line in selection.Lines)
            response.Line(line);

        return response.ToString();
    }

    private static LineSelection Collect(string text, LineRange range)
    {
        var total = CountLines(text);
        var lines = new List<string>(Math.Min(range.MaxLines, 512));
        var budget = MaxResponseCharacters;
        var number = 0;

        foreach (var line in text.AsSpan().EnumerateLines())
        {
            number++;

            if (number > total)
                break;

            if (!range.Covers(number) || lines.Count >= range.MaxLines || budget <= 0)
                continue;

            lines.Add(Numbered(number, line, budget));
            budget -= Math.Min(line.Length, budget);
        }

        return new LineSelection(lines, total);
    }

    private static string Numbered(int number, ReadOnlySpan<char> line, int budget) => line.Length <= budget
        ? string.Create(CultureInfo.InvariantCulture, $"{number}: {line}")
        : string.Create(CultureInfo.InvariantCulture, $"{number}: {line[..budget]}... (+{line.Length - budget} chars)");

    public readonly record struct ReadRequest(LineRange Range, bool Headings, string? Section);

    public readonly record struct EditRequest(string OldText, string NewText, string? Section, bool DryRun, bool Force, bool Verbose);

    public readonly record struct LineRange(int Start, int End, int MaxLines)
    {
        public bool Covers(int line) => line >= Math.Max(1, Start) && line <= (End <= 0 ? int.MaxValue : End);
    }

    private readonly record struct LineSelection(IReadOnlyList<string> Lines, int TotalLines);
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

        var updated = workspace.Solution.WithDocumentText(document.Id, SourceText.From(content, Encoding.UTF8));
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

    private static string Outside(string full) => full + "  outside-workspace";

    private static async Task<Result<string>> PresentFileAsync(
            string full,
            string label,
            ReadRequest request,
            CancellationToken cancellationToken)
    {
        if (!File.Exists(full))
            return Result.Fail<string>(Errors.DocumentNotFound(label));

        if (BinaryContent.Reject(full, label) is { } binary)
            return binary;

        var text = await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);

        return Present(full, label, text, request);
    }
}
