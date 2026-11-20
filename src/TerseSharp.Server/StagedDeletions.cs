namespace TerseSharp.Server;

internal static class StagedDeletions
{
    public static async Task<Result<string>> NotedAsync(string root, Func<Task<Result<string>>> move, bool dryRun, CancellationToken cancellationToken)
    {
        if (dryRun)
            return await move().ConfigureAwait(false);

        var before = await StagedButDeletedAsync(root, cancellationToken).ConfigureAwait(false);
        var result = await move().ConfigureAwait(false);

        if (!result.IsOk || Appeared(before, await StagedButDeletedAsync(root, cancellationToken).ConfigureAwait(false)) is not [_, ..] stale)
            return result;

        return Result.Ok(result.Value + "\n" + Warning(stale));
    }

    internal static string Warning(IReadOnlyList<string> stale) => string.Create(
        CultureInfo.InvariantCulture,
        $"WARNING {stale.Count} file(s) still staged at a path this change removed from disk: {string.Join(", ", stale)} - a commit would carry the old path; git rm --cached it, or git add the old and new paths together so git records the move");

    internal static string[] Appeared(string[] before, string[] after) => [.. after.Except(before, StringComparer.Ordinal)];

    internal static string[] Intersect(string deleted, string staged)
    {
        var stagedPaths = staged.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);

        return [.. deleted.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(stagedPaths.Contains)];
    }

    private static async Task<string[]> StagedButDeletedAsync(string root, CancellationToken cancellationToken)
    {
        var deleted = await GitRunner.ReadAsync(root, ["diff", "--name-only", "--relative", "--diff-filter=D"], cancellationToken).ConfigureAwait(false);
        var staged = await GitRunner.ReadAsync(root, ["diff", "--cached", "--name-only", "--relative"], cancellationToken).ConfigureAwait(false);

        return deleted.IsOk && staged.IsOk ? Intersect(deleted.Value!, staged.Value!) : [];
    }
}
