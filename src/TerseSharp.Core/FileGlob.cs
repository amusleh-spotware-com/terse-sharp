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

        Fragment(text, normalized.AsSpan(), 0);

        return text.Append('$').ToString();
    }

    private static int Append(StringBuilder text, ReadOnlySpan<char> glob, int depth) => glob switch
    {
        ['*', '*', '/', ..] => Write(text, "(?:.*/)?", 3),
        ['*', '*', ..] => Write(text, ".*", 2),
        ['*', ..] => Write(text, "[^/]*", 1),
        ['?', ..] => Write(text, "[^/]", 1),
        ['{', ..] when depth < MaxBraceDepth => Alternation(text, glob, depth),
        _ => Write(text, Regex.Escape(glob[0].ToString()), 1),
    };

    private static int Write(StringBuilder text, string value, int consumed)
    {
        text.Append(value);

        return consumed;
    }

    private static void Fragment(StringBuilder text, ReadOnlySpan<char> glob, int depth)
    {
        var index = 0;

        while (index < glob.Length)
            index += Append(text, glob[index..], depth);
    }

    private static int Alternation(StringBuilder text, ReadOnlySpan<char> glob, int depth)
    {
        var close = Closing(glob);

        if (close < 0)
            return Write(text, Regex.Escape("{"), 1);

        text.Append("(?:");
        Alternatives(text, glob[1..close], depth + 1);
        text.Append(')');

        return close + 1;
    }

    private static void Alternatives(StringBuilder text, ReadOnlySpan<char> body, int depth)
    {
        var remaining = body;

        while (true)
        {
            var cut = Separator(remaining);

            if (cut < 0)
            {
                Fragment(text, remaining, depth);

                return;
            }

            Fragment(text, remaining[..cut], depth);
            text.Append('|');
            remaining = remaining[(cut + 1)..];
        }
    }

    private static int Separator(ReadOnlySpan<char> body)
    {
        var depth = 0;

        for (var index = 0; index < body.Length; index++)
        {
            if (body[index] is ',' && depth is 0)
                return index;

            depth += body[index] switch { '{' => 1, '}' => -1, _ => 0 };
        }

        return -1;
    }

    private static int Closing(ReadOnlySpan<char> glob)
    {
        var depth = 0;

        for (var index = 0; index < glob.Length; index++)
        {
            depth += glob[index] switch { '{' => 1, '}' => -1, _ => 0 };

            if (depth is 0)
                return index;
        }

        return -1;
    }

    private const int MaxBraceDepth = 16;
}
