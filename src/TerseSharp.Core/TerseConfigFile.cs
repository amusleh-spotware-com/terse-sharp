namespace TerseSharp.Core;

public static class TerseConfigFile
{
    public const string FileName = ".terse.json";

    public const int MaxBytes = 64 * 1024;

    public static Task<IReadOnlyList<string>> ChainAsync(string directory, CancellationToken cancellationToken) =>
        ChainAsync(directory, Home(), cancellationToken);

    public static Task<IReadOnlyList<string>> ChainAsync(string directory, string? home, CancellationToken cancellationToken) =>
        ChainAsync(directory, home, inheritMainCheckout: false, cancellationToken);

    public static Task<IReadOnlyList<string>> PolicyChainAsync(string directory, CancellationToken cancellationToken) =>
        ChainAsync(directory, Home(), inheritMainCheckout: true, cancellationToken);

    public static Task<IReadOnlyList<string>> PolicyChainAsync(string directory, string? home, CancellationToken cancellationToken) =>
        ChainAsync(directory, home, inheritMainCheckout: true, cancellationToken);

    private static async Task<IReadOnlyList<string>> ChainAsync(string directory, string? home, bool inheritMainCheckout, CancellationToken cancellationToken)
    {
        var found = new List<string>();
        var repository = Directory.Exists(directory) ? Walked(new DirectoryInfo(directory), found) : null;

        if (inheritMainCheckout && repository is not null && await LinkedWorktree.MainCheckoutAsync(repository, cancellationToken).ConfigureAwait(false) is { } main)
            Walked(new DirectoryInfo(Path.Combine(main, Path.GetRelativePath(repository, Path.GetFullPath(directory)))), found);

        if (Global(home) is { } global && !found.Contains(global, StringComparer.OrdinalIgnoreCase))
            found.Add(global);

        found.Reverse();

        return found;
    }

    public static string Home() =>
        Environment.GetEnvironmentVariable("TERSE_HOME") is { Length: > 0 } overridden
            ? overridden
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string Oversized(long length) => string.Create(
        CultureInfo.InvariantCulture,
        $"it is {length} bytes, past the {MaxBytes}-byte ceiling");

    private static string? Global(string? home)
    {
        if (home is not { Length: > 0 })
            return null;

        var candidate = Path.Combine(home, FileName);

        return File.Exists(candidate) ? candidate : null;
    }

    private static bool AtRepositoryRoot(DirectoryInfo directory) =>
        Directory.Exists(Path.Combine(directory.FullName, ".git"))
            || File.Exists(Path.Combine(directory.FullName, ".git"));

    private static string? Walked(DirectoryInfo start, List<string> found)
    {
        for (var current = start; current is not null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, FileName);

            if (File.Exists(candidate) && !found.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                found.Add(candidate);

            if (AtRepositoryRoot(current))
                return current.FullName;
        }

        return null;
    }
}
