namespace TerseSharp.Core;

public static class MissingDirectory
{
    public static TerseError Refused(string root) => File.Exists(Path.GetFullPath(root))
        ? NotADirectory(root)
        : Named(root, NearestAncestor(Path.GetFullPath(root)));

    private static TerseError NotADirectory(string root) => new(
        TerseErrorCode.DocumentNotFound,
        string.Create(CultureInfo.InvariantCulture, $"'{root}' is a file, not a directory"),
        string.Create(CultureInfo.InvariantCulture, $"pass the directory that holds it as root=, or read it with read_text path=\"{root}\""));

    private static TerseError Named(string root, string? ancestor) => ancestor is null
        ? new(
            TerseErrorCode.DocumentNotFound,
            string.Create(CultureInfo.InvariantCulture, $"directory '{root}' does not exist, and neither does any directory above it"),
            "check the drive or volume in root=, or drop root= to answer about the loaded workspace")
        : new(
            TerseErrorCode.DocumentNotFound,
            string.Create(CultureInfo.InvariantCulture, $"directory '{root}' does not exist - the nearest existing ancestor is '{ancestor}'"),
            string.Create(CultureInfo.InvariantCulture, $"list find_files root=\"{ancestor}\" to find the name you meant, or drop root= to answer about the loaded workspace"));

    private static string? NearestAncestor(string full)
    {
        for (var parent = Path.GetDirectoryName(full); parent is not null; parent = Path.GetDirectoryName(parent))
        {
            if (Directory.Exists(parent))
                return parent;
        }

        return null;
    }
}
