using System.Text;
using System.Text.RegularExpressions;

namespace TerseSharp.Core;

public readonly record struct FileGlob(Regex Pattern, bool MatchesPath)
{
    private const int MaxStackPath = 512;

    public static FileGlob Compile(string glob) => new(
        new Regex(Translate(glob), RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        IsPathPattern(glob));

    public static bool IsPathPattern(string glob) =>
        glob.Contains('/', StringComparison.Ordinal) || glob.Contains('\\', StringComparison.Ordinal);

    public bool Matches(ReadOnlySpan<char> path)
    {
        if (!path.Contains('\\'))
            return Pattern.IsMatch(path);

        if (path.Length > MaxStackPath)
            return Pattern.IsMatch(path.ToString().Replace('\\', '/'));

        Span<char> buffer = stackalloc char[MaxStackPath];
        var target = buffer[..path.Length];

        path.CopyTo(target);
        target.Replace('\\', '/');

        return Pattern.IsMatch(target);
    }

    public bool MatchesFile(string root, string file) =>
        MatchesPath ? Matches(Path.GetRelativePath(root, file)) : Matches(Path.GetFileName(file.AsSpan()));

    public bool MatchesRelative(ReadOnlySpan<char> relativePath) =>
        MatchesPath ? Matches(relativePath) : Matches(Path.GetFileName(relativePath));

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

    public static TerseError? Unsupported(string? glob, string parameter) =>
        glob is not null && glob.AsSpan().IndexOfAny('{', '}') >= 0
            ? Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{parameter}' contains a brace, and brace expansion is not implemented - this glob would match nothing rather than the alternatives it names"),
                "the supported syntax is ** for any directories and * or ? within one segment; send one call per alternative, or widen the glob - **/*.md rather than **/*.{md,yml}")
            : null;
}
