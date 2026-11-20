namespace TerseSharp.Core;

internal static class ErrorSites
{
    private const int Shown = 3;

    private const string NoFile = "(no file)";

    public static string? Describe(IReadOnlyDictionary<string, int> errors, int introduced)
    {
        if (errors.Count is 0 || introduced > 0)
            return null;

        var ranked = PerFile(errors).OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        var shown = string.Join(", ", ranked.Take(Shown).Select(entry => string.Create(CultureInfo.InvariantCulture, $"{entry.Key} {entry.Value}")));

        return ranked.Length > Shown
            ? string.Create(CultureInfo.InvariantCulture, $"errors in scope: {shown} +{ranked.Length - Shown} more file(s)")
            : "errors in scope: " + shown;
    }

    public static string Joined(string counters, string? sites) =>
        sites is null || counters.Length is 0 ? counters : counters + "\n" + sites;

    private static Dictionary<string, int> PerFile(IReadOnlyDictionary<string, int> errors)
    {
        var perFile = new Dictionary<string, int>(errors.Count, StringComparer.Ordinal);

        foreach (var (key, count) in errors)
        {
            var file = FileOf(key);
            perFile[file] = perFile.GetValueOrDefault(file) + count;
        }

        return perFile;
    }

    private static string FileOf(string key)
    {
        var rest = key.AsSpan(key.IndexOf(' ', StringComparison.Ordinal) + 1);
        var end = rest.IndexOf(": ", StringComparison.Ordinal);

        return end > 0 ? new string(rest[..end]) : NoFile;
    }
}
