namespace TerseSharp.Core;

public static class LandedWrites
{
    public static async Task<Result<string>> GuardedAsync(
        string root,
        IReadOnlyList<FileService.FileWrite> targets,
        bool dryRun,
        Func<Task<Result<string>>> write,
        CancellationToken cancellationToken)
    {
        try
        {
            return await write().ConfigureAwait(false);
        }
        catch (Exception exception) when (!dryRun && Errors.IsBuildHostFailure(exception))
        {
            var landing = await LandingAsync(root, targets, cancellationToken).ConfigureAwait(false);

            if (landing.Landed is [])
                throw;

            return Result.Fail<string>(Errors.TransientLanded(exception, landing.Landed, landing.Missing));
        }
    }

    public static async Task<WriteLanding> LandingAsync(string root, IReadOnlyList<FileService.FileWrite> targets, CancellationToken cancellationToken)
    {
        var landed = new List<string>(targets.Count);
        var missing = new List<string>(targets.Count);

        foreach (var target in targets)
        {
            var full = Path.GetFullPath(target.Path, root);

            (await HoldsAsync(full, target.Content, cancellationToken).ConfigureAwait(false) ? landed : missing).Add(PositionFormat.Relative(root, full));
        }

        return new(landed, missing);
    }

    public static bool Same(ReadOnlySpan<char> disk, ReadOnlySpan<char> requested)
    {
        var (left, right) = (0, 0);

        while (true)
        {
            (left, right) = (Skip(disk, left), Skip(requested, right));

            if (left == disk.Length || right == requested.Length)
                return left == disk.Length && right == requested.Length;

            if (disk[left++] != requested[right++])
                return false;
        }
    }

    private static async Task<bool> HoldsAsync(string full, string content, CancellationToken cancellationToken) =>
        File.Exists(full) && Same(await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false), content);

    private static int Skip(ReadOnlySpan<char> text, int index) =>
        index + 1 < text.Length && text[index] is '\r' && text[index + 1] is '\n' ? index + 1 : index;
}

public sealed record WriteLanding(IReadOnlyList<string> Landed, IReadOnlyList<string> Missing);
