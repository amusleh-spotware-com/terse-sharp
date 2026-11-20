namespace TerseSharp.Core;

public static class LinkedWorktree
{
    public static async Task<string?> MainCheckoutAsync(string repository, CancellationToken cancellationToken)
    {
        if (Pointed(repository, await ContentAsync(Path.Combine(repository, ".git"), cancellationToken).ConfigureAwait(false), "gitdir:") is not { } own
            || Pointed(own, await ContentAsync(Path.Combine(own, "commondir"), cancellationToken).ConfigureAwait(false), string.Empty) is not { } common)
        {
            return null;
        }

        var trimmed = Path.TrimEndingDirectorySeparator(common);

        return Path.GetFileName(trimmed.AsSpan()) is ".git" && Directory.Exists(trimmed) ? Path.GetDirectoryName(trimmed) : null;
    }

    private static string? Pointed(string relativeTo, string? content, string prefix)
    {
        var line = content.AsSpan().Trim();

        return content is not null && line.StartsWith(prefix, StringComparison.Ordinal) && line[prefix.Length..].Trim() is { IsEmpty: false } target
            ? Path.GetFullPath(target.ToString(), relativeTo)
            : null;
    }

    private static async Task<string?> ContentAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
