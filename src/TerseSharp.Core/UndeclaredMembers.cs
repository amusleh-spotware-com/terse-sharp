namespace TerseSharp.Core;

internal static class UndeclaredMembers
{
    public static string[]? Hint(string[] errors)
    {
        if (errors.Length is 0)
            return null;

        var members = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var error in errors)
        {
            if (Member(error) is not { } member)
                return null;

            members.Add(member);
        }

        return [.. members];
    }

    public static string Remedy(IReadOnlyList<string> members, string? tool) =>
        "the edit uses " + string.Join(", ", members) + ", which nothing declares - a typo is fixed in the edit; a member another pending edit adds: " + tool switch
        {
            "replace_symbol" => "retry with the retryWith token below plus add=[\"<that declaration>\"] addTo=\"<its type>\", so both land in ONE compile-gated edit",
            "edit_text" => "send the edit that declares it in the same edits=[...] batch: every .cs entry shares ONE compile gate",
            "write_text" => "send the file that declares it in the same files=[...] batch: every .cs entry shares ONE compile gate",
            _ => "declare it first with add_member, then retry with the retryWith token below",
        };

    private static string? Member(string error) =>
        error.StartsWith("CS0117 ", StringComparison.Ordinal) || error.StartsWith("CS1061 ", StringComparison.Ordinal)
            ? Qualified(error)
            : null;

    private static string? Qualified(string error)
    {
        var message = error.AsSpan();
        var type = Quoted(ref message);
        var member = Quoted(ref message);

        return type.IsEmpty || member.IsEmpty ? null : string.Concat(LastSegment(type), ".", member);
    }

    private static ReadOnlySpan<char> Quoted(ref ReadOnlySpan<char> text)
    {
        var open = text.IndexOf('\'');
        var close = open < 0 ? -1 : text[(open + 1)..].IndexOf('\'');

        if (close < 0)
            return [];

        var quoted = text.Slice(open + 1, close);
        text = text[(open + close + 2)..];

        return quoted;
    }

    private static ReadOnlySpan<char> LastSegment(ReadOnlySpan<char> type)
    {
        var open = type.IndexOf('<');
        var name = open < 0 ? type : type[..open];

        return name[(name.LastIndexOf('.') + 1)..];
    }
}
