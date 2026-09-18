namespace TerseSharp.Core;

public static class TerseConfigFile
{
    public const string FileName = ".terse.json";

    public const int MaxBytes = 64 * 1024;

    public static IReadOnlyList<string> Chain(string directory) => Chain(directory, Home());

    public static IReadOnlyList<string> Chain(string directory, string? home)
    {
        var found = new List<string>();
        var current = Directory.Exists(directory) ? new DirectoryInfo(directory) : null;

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, FileName);

            if (File.Exists(candidate))
                found.Add(candidate);

            current = AtRepositoryRoot(current) ? null : current.Parent;
        }

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
}
