using System.Text;

namespace TerseSharp.Server;

internal static class IndexWrite
{
    private const string RegularFile = "100644";

    public static async Task<string> StageAsync(string root, string path, string content, bool dryRun, bool verbose, CancellationToken cancellationToken)
    {
        if (Relative(root, path) is not { } relative)
            return Errors.OutOfWorkspace(path).Render();

        var indexed = await GitRunner.ShowAsync(root, string.Empty, relative, cancellationToken).ConfigureAwait(false);
        var before = indexed.IsOk ? indexed.Value! : string.Empty;

        if (dryRun)
            return "dryRun stage  " + relative + "  changedLines=" + Changed(before, content) + "\n" + UnifiedDiff.Between(relative, before, content);

        var staged = await ApplyAsync(root, relative, content, cancellationToken).ConfigureAwait(false);

        return !staged.IsOk
            ? staged.Error!.Render()
            : "staged  " + relative + "  changedLines=" + Changed(before, content) + "  working tree untouched" + (verbose ? "\n" + UnifiedDiff.Between(relative, before, content) : string.Empty);
    }

    public static async Task<string> UnstageAsync(string root, string path, string reference, CancellationToken cancellationToken)
    {
        if (Relative(root, path) is not { } relative)
            return Errors.OutOfWorkspace(path).Render();

        var reset = await GitRunner.ReadAsync(root, ["reset", "-q", reference, "--", relative], cancellationToken).ConfigureAwait(false);

        return reset.IsOk
            ? "unstaged  " + relative + "  index entry is " + reference + "'s again, working tree untouched"
            : reset.Error!.Render();
    }

    private static async Task<Result<string>> ApplyAsync(string root, string relative, string content, CancellationToken cancellationToken)
    {
        var blob = await BlobAsync(root, relative, content, cancellationToken).ConfigureAwait(false);

        if (!blob.IsOk)
            return blob;

        var mode = await ModeAsync(root, relative, cancellationToken).ConfigureAwait(false);
        var sha = blob.Value!.Trim();

        return await GitRunner.ReadAsync(root, ["update-index", "--add", "--cacheinfo", mode + "," + sha + "," + relative], cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Result<string>> BlobAsync(string root, string relative, string content, CancellationToken cancellationToken)
    {
        var blob = Path.Join(Path.GetTempPath(), "terse-stage-" + Guid.NewGuid().ToString("N") + ".blob");

        try
        {
            await File.WriteAllBytesAsync(blob, Encoding.UTF8.GetBytes(content), cancellationToken).ConfigureAwait(false);

            return await GitRunner.ReadAsync(root, ["hash-object", "-w", "--path=" + relative, blob], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(blob);
        }
    }

    private static async Task<string> ModeAsync(string root, string relative, CancellationToken cancellationToken)
    {
        var listed = await GitRunner.ReadAsync(root, ["ls-files", "--stage", "--", relative], cancellationToken).ConfigureAwait(false);
        var line = listed.IsOk ? listed.Value!.AsSpan().Trim() : default;
        var space = line.IndexOf(' ');

        return space > 0 ? new string(line[..space]) : RegularFile;
    }

    private static string? Relative(string root, string path)
    {
        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));

        return PathBoundary.Contains(root, full) ? Path.GetRelativePath(root, full).Replace('\\', '/') : null;
    }

    private static string Changed(string before, string after) =>
        UnifiedDiff.ChangedLines(before, after).ToString(CultureInfo.InvariantCulture);
}
