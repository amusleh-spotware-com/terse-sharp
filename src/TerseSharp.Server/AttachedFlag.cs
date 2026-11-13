namespace TerseSharp.Server;

internal static class AttachedFlag
{
    public static bool Binds(ReadOnlySpan<char> before, ReadOnlySpan<char> after) =>
        before is [.. ([] or [.., ' ' or '\t']), '-', 'v' or 'a'] && Ended(after);

    private static bool Ended(ReadOnlySpan<char> after) => after.IsEmpty || !(char.IsAsciiLetterOrDigit(after[0]) || after[0] is '_');
}
