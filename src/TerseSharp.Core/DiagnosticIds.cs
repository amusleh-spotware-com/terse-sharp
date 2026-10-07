namespace TerseSharp.Core;

internal static class DiagnosticIds
{
    public static ReadOnlySpan<char> Stem(string entry) => entry.AsSpan().TrimEnd('*');

    public static bool Matches(string id, string entry) =>
        IsPrefix(entry)
            ? id.AsSpan().StartsWith(Stem(entry), StringComparison.OrdinalIgnoreCase)
            : string.Equals(id, entry, StringComparison.OrdinalIgnoreCase);

    public static bool MatchesAny(string id, IReadOnlyList<string> entries)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            if (Matches(id, entries[index]))
                return true;
        }

        return false;
    }

    public static bool IsDeclared(string entry, HashSet<string> declared) =>
        IsPrefix(entry) ? declared.Any(id => Matches(id, entry)) : declared.Contains(entry);

    public static bool IsCompilerId(string entry) =>
        IsPrefix(entry)
            ? "CS".AsSpan().StartsWith(Stem(entry), StringComparison.OrdinalIgnoreCase) || IsCompilerNumber(Stem(entry))
            : entry.Length > 2 && IsCompilerNumber(entry);

    public static bool IsDeadCodeId(string entry) => Matches(DeadCodeService.RuleId, entry);

    private static bool IsPrefix(string entry) => entry.EndsWith('*');

    private static bool IsCompilerNumber(ReadOnlySpan<char> id) =>
        id.StartsWith("CS", StringComparison.OrdinalIgnoreCase)
        && !id[2..].ContainsAnyExceptInRange('0', '9');
}
