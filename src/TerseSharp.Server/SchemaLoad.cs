using System.Buffers;

namespace TerseSharp.Server;

internal static class SchemaLoad
{
    private const string ToolPrefix = "mcp__" + ClientRegistrar.ServerName + "__";

    private const string Clause = "  - if its schema is not loaded: ToolSearch select:";

    private static readonly SearchValues<char> ToolNameCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyz_");

    public static string For(string routing)
    {
        var calls = Calls(routing.AsSpan());
        var names = new List<string>(4);
        var (follows, quoted) = (true, false);

        foreach (var range in calls.Split(' '))
        {
            var word = calls[range].TrimEnd(',');

            if (follows && Fresh(names, word))
                names.Add(string.Concat(ToolPrefix, word));

            quoted ^= word.Count('"') % 2 is 1;
            follows = Follows(follows, quoted, word);
        }

        return names.Count is 0 ? string.Empty : Clause + string.Join(',', names);
    }

    private static ReadOnlySpan<char> Calls(ReadOnlySpan<char> routing) => Before(Before(routing, "  |  "), "  then re-issue");

    private static ReadOnlySpan<char> Before(ReadOnlySpan<char> text, string marker) =>
        text.IndexOf(marker, StringComparison.Ordinal) is var at and >= 0 ? text[..at] : text;

    private static bool Follows(bool follows, bool quoted, ReadOnlySpan<char> word) =>
        !quoted && (word.IsEmpty ? follows : word is "then");

    private static bool Fresh(List<string> names, ReadOnlySpan<char> word)
    {
        if (word.IsEmpty || word.ContainsAnyExcept(ToolNameCharacters))
            return false;

        foreach (var name in names)
        {
            if (word.SequenceEqual(name.AsSpan(ToolPrefix.Length)))
                return false;
        }

        return true;
    }
}
