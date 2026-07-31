namespace TerseSharp.Core;

public static class FileService
{
    private const int MaxResponseCharacters = 128 * 1024;

    private const int MaxScannedLines = 5_000_000;

    public static Result<string> ReadText(
        LoadedWorkspace workspace,
        string path,
        int startLine,
        int endLine,
        int maxLines,
        CancellationToken cancellationToken)
    {
        var resolved = PathGuard.Resolve(workspace, path);

        if (!resolved.IsOk)
            return Result.Fail<string>(resolved.Error!);

        var full = resolved.Value!;

        if (!File.Exists(full))
            return Result.Fail<string>(Errors.DocumentNotFound(path));

        return BinaryContent.Reject(full, path)
            ?? Result.Ok(Render(path, full, new LineRange(startLine, endLine, maxLines), cancellationToken));
    }

    public static Result<string> WriteText(LoadedWorkspace workspace, string path, string content, bool dryRun, bool force)
    {
        var resolved = PathGuard.Resolve(workspace, path);

        if (!resolved.IsOk)
            return Result.Fail<string>(resolved.Error!);

        if (SourceFile.Reject(path, force) is { } refusal)
            return refusal;

        var full = resolved.Value!;
        var before = File.Exists(full) ? File.ReadAllText(full) : string.Empty;

        if (!dryRun)
            WriteAtomic(full, content);

        return Result.Ok(DiffResponse("write_text", path, before, content, dryRun));
    }

    public static Result<string> EditText(LoadedWorkspace workspace, string path, string oldText, string newText, bool dryRun, bool force)
    {
        var resolved = PathGuard.Resolve(workspace, path);

        if (!resolved.IsOk)
            return Result.Fail<string>(resolved.Error!);

        if (SourceFile.Reject(path, force) is { } refusal)
            return refusal;

        var full = resolved.Value!;

        if (!File.Exists(full))
            return Result.Fail<string>(Errors.DocumentNotFound(path));

        return Replace(full, path, oldText, newText, dryRun);
    }

    private static Result<string> Replace(string full, string path, string oldText, string newText, bool dryRun)
    {
        var before = File.ReadAllText(full);
        var occurrences = Count(before, oldText);

        if (occurrences is not 1)
            return Result.Fail<string>(AmbiguousMatch(occurrences));

        var after = before.Replace(oldText, newText, StringComparison.Ordinal);

        if (!dryRun)
            WriteAtomic(full, after);

        return Result.Ok(DiffResponse("edit_text", path, before, after, dryRun));
    }

    private static TerseError AmbiguousMatch(int occurrences) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"oldText matched {occurrences} times, expected exactly 1"),
        "include more surrounding text so the match is unique");

    private static int Count(string text, string value) =>
        string.IsNullOrEmpty(value) ? 0 : (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;

    private static void WriteAtomic(string path, string content) => AtomicWrite.Text(path, content);

    private static string DiffResponse(string tool, string path, string before, string after, bool dryRun)
    {
        var response = new ResponseBuilder(tool, dryRun ? "dryRun" : "applied");

        response.Summary(1, 1, "files changed");
        response.Line(UnifiedDiff.Between(path, before, after));
        response.Line(string.Create(CultureInfo.InvariantCulture, $"changedLines={UnifiedDiff.ChangedLines(before, after)}"));

        return response.ToString();
    }

    private static string Render(string path, string full, LineRange range, CancellationToken cancellationToken)
    {
        var selection = Collect(full, range, cancellationToken);
        var response = new ResponseBuilder("read_text", path);

        response.Summary(selection.Lines.Count, selection.TotalLines, "lines");

        foreach (var line in selection.Lines)
            response.Line(line);

        return response.ToString();
    }

    private static LineSelection Collect(string full, LineRange range, CancellationToken cancellationToken)
    {
        var lines = new List<string>(Math.Min(range.MaxLines, 512));
        var budget = MaxResponseCharacters;
        var number = 0;

        foreach (var line in File.ReadLines(full))
        {
            cancellationToken.ThrowIfCancellationRequested();
            number++;

            if (number > MaxScannedLines)
                break;

            if (range.Covers(number) && lines.Count < range.MaxLines && budget > 0)
            {
                budget -= line.Length;
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"{number}: {line}"));
            }
        }

        return new LineSelection(lines, number);
    }

    private readonly record struct LineRange(int Start, int End, int MaxLines)
    {
        public bool Covers(int line) => line >= Math.Max(1, Start) && line <= (End <= 0 ? int.MaxValue : End);
    }

    private readonly record struct LineSelection(IReadOnlyList<string> Lines, int TotalLines);
}
