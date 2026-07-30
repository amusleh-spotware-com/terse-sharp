namespace TerseSharp.Core;

public static class FileService
{
    public static Result<string> ReadText(LoadedWorkspace workspace, string path, int startLine, int endLine)
    {
        var resolved = PathGuard.Resolve(workspace, path);

        if (!resolved.IsOk)
            return Result.Fail<string>(resolved.Error!);

        var full = resolved.Value!;

        if (!File.Exists(full))
            return Result.Fail<string>(Errors.DocumentNotFound(path));

        return BinaryContent.Reject(full, path) ?? Result.Ok(Render(path, File.ReadAllLines(full), startLine, endLine));
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

    private static string Render(string path, string[] lines, int startLine, int endLine)
    {
        var from = Math.Max(1, startLine);
        var to = endLine <= 0 ? lines.Length : Math.Min(endLine, lines.Length);
        var response = new ResponseBuilder("read_text", path);

        response.Summary(Math.Max(0, to - from + 1), lines.Length, "lines");

        for (var index = from; index <= to; index++)
            response.Line(string.Create(CultureInfo.InvariantCulture, $"{index}: {lines[index - 1]}"));

        return response.ToString();
    }
}
