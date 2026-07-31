using System.Text;
using System.Text.RegularExpressions;

namespace TerseSharp.Core;

public readonly record struct FileGlob(Regex Pattern, bool MatchesPath)
{
    public static FileGlob Compile(string glob) => new(
        new Regex(Translate(glob), RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        IsPathPattern(glob));

    public static bool IsPathPattern(string glob) =>
        glob.Contains('/', StringComparison.Ordinal) || glob.Contains('\\', StringComparison.Ordinal);

    public bool Matches(string path) => Pattern.IsMatch(path.Replace('\\', '/'));

    public bool MatchesFile(string root, string file) =>
        Matches(MatchesPath ? Path.GetRelativePath(root, file) : Path.GetFileName(file));

    public bool MatchesRelative(string relativePath) =>
        Matches(MatchesPath ? relativePath : Path.GetFileName(relativePath));

    private static string Translate(string glob)
    {
        var text = new StringBuilder("^");
        var normalized = glob.Replace('\\', '/');
        var index = 0;

        while (index < normalized.Length)
            index += Append(text, normalized, index);

        return text.Append('$').ToString();
    }

    private static int Append(StringBuilder text, string glob, int index) => glob.AsSpan(index) switch
    {
        ['*', '*', '/', ..] => Write(text, "(?:.*/)?", 3),
        ['*', '*', ..] => Write(text, ".*", 2),
        ['*', ..] => Write(text, "[^/]*", 1),
        ['?', ..] => Write(text, "[^/]", 1),
        var remaining => Write(text, Regex.Escape(remaining[0].ToString()), 1),
    };

    private static int Write(StringBuilder text, string value, int consumed)
    {
        text.Append(value);

        return consumed;
    }
}
