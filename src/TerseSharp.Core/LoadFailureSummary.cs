using System.Text;

namespace TerseSharp.Core;

public readonly record struct LoadFailureGroup(string Project, int Count);

public static class LoadFailureSummary
{
    private const int MaxUnattributedLength = 120;

    public static LoadFailureGroup[] Group(IReadOnlyList<string> failures)
    {
        if (failures.Count is 0)
            return [];

        var groups = new List<LoadFailureGroup>(failures.Count);

        foreach (var failure in failures)
            Add(groups, Name(failure));

        return [.. groups];
    }

    public static ReadOnlySpan<char> ProjectOf(ReadOnlySpan<char> failure)
    {
        var remaining = failure;

        while (remaining.IndexOf('\'') is var open and >= 0)
        {
            var rest = remaining[(open + 1)..];
            var close = rest.IndexOf('\'');

            if (close < 0)
                return [];

            if (rest[..close] is var quoted && quoted.EndsWith("proj", StringComparison.OrdinalIgnoreCase))
                return FileName(quoted);

            remaining = rest[(close + 1)..];
        }

        return [];
    }

    private static ReadOnlySpan<char> FileName(ReadOnlySpan<char> path) =>
        path.LastIndexOfAny('/', '\\') is var separator and >= 0 ? path[(separator + 1)..] : path;

    private static string Name(string failure) =>
        ProjectOf(failure) is { IsEmpty: false } project
            ? new string(project)
            : Shorten(failure);

    private static string Shorten(string failure) =>
        failure.Length <= MaxUnattributedLength ? failure : string.Concat(failure.AsSpan(0, MaxUnattributedLength), "...");

    private static void Add(List<LoadFailureGroup> groups, string project)
    {
        for (var index = 0; index < groups.Count; index++)
        {
            if (!string.Equals(groups[index].Project, project, StringComparison.Ordinal))
                continue;

            groups[index] = groups[index] with { Count = groups[index].Count + 1 };

            return;
        }

        groups.Add(new LoadFailureGroup(project, 1));
    }

    public static string Relative(string message, string root)
    {
        if (root is not { Length: > 0 } || !message.Contains(root, StringComparison.OrdinalIgnoreCase))
            return message;

        var builder = new StringBuilder(message.Length);
        var at = Stripped(message.AsSpan(), root, builder);

        return builder.Append(message.AsSpan(at)).ToString();
    }

    private static int Stripped(ReadOnlySpan<char> span, string root, StringBuilder builder)
    {
        var at = 0;

        while (span[at..].IndexOf(root, StringComparison.OrdinalIgnoreCase) is var found and >= 0)
        {
            var after = at + found + root.Length;
            var bounded = Bounded(span, after);

            builder.Append(span[at..(bounded ? at + found : after)]);
            at = bounded ? after + Separator(span, after) : after;
        }

        return at;
    }

    private static bool Bounded(ReadOnlySpan<char> span, int after) =>
        after >= span.Length || IsSeparator(span[after]) || !IsPathCharacter(span[after]);


    private static int Separator(ReadOnlySpan<char> span, int after) =>
        after < span.Length && IsSeparator(span[after]) ? 1 : 0;


    private static bool IsSeparator(char character) =>
        character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar;


    private static bool IsPathCharacter(char character) =>
        char.IsLetterOrDigit(character) || character is '.' or '-' or '_' or '~' or ' ';
}
