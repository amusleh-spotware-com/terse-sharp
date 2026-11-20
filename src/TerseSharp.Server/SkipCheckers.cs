namespace TerseSharp.Server;

internal static class SkipCheckers
{
    public static string Named(string[] skipped) =>
        string.Join(", ", skipped.GroupBy(Checker, StringComparer.Ordinal).Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Key} checks {group.Count()}")));

    private static string Checker(string path) => Path.GetExtension(path.AsSpan()) switch
    {
        var extension when Any(extension, ".resx", ".resw") => "resx_validate",
        var extension when Any(extension, ".xaml", ".axaml", ".paml") => "xaml_validate",
        var extension when Any(extension, ".razor", ".cshtml") => "razor_validate",
        _ => "no terse gate",
    };

    private static bool Any(ReadOnlySpan<char> extension, params ReadOnlySpan<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (extension.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
