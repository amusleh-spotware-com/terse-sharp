namespace TerseSharp.Core;

public static class TerseConfigFile
{
    public const string FileName = ".terse.json";

    public const int MaxBytes = 64 * 1024;

    public static string? Find(string directory)
    {
        var current = Directory.Exists(directory) ? new DirectoryInfo(directory) : null;

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, FileName);

            if (File.Exists(candidate))
                return candidate;

            current = AtRepositoryRoot(current) ? null : current.Parent;
        }

        return null;
    }

    public static string Oversized(long length) => string.Create(
        CultureInfo.InvariantCulture,
        $"it is {length} bytes, past the {MaxBytes}-byte ceiling");

    private static bool AtRepositoryRoot(DirectoryInfo directory) =>
        Directory.Exists(Path.Combine(directory.FullName, ".git"))
            || File.Exists(Path.Combine(directory.FullName, ".git"));
}
