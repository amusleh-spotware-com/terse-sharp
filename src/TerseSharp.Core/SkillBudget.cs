namespace TerseSharp.Core;

public static class SkillBudget
{
    public const int Tokens = 27200;

    public static int Estimated(int characters) => (characters + 3) / 4;

    public static int Used(ReadOnlySpan<char> text) => Estimated(text.Length - text.Count('\r'));

    public static string Stamp(ReadOnlySpan<char> path, bool requested, ReadOnlySpan<char> text) =>
        requested && IsShippedSkill(path) ? Rendered(Used(text)) : string.Empty;

    public static bool IsShippedSkill(ReadOnlySpan<char> path) =>
        Segment(ref path, "SKILL.md") && Segment(ref path, "Assets") && Segment(ref path, "TerseSharp.Server");

    private static bool Segment(ref ReadOnlySpan<char> path, ReadOnlySpan<char> expected)
    {
        var cut = path.LastIndexOfAny('/', '\\');
        var name = path[(cut + 1)..];
        path = cut < 0 ? [] : path[..cut];

        return name.Equals(expected, StringComparison.Ordinal);
    }

    private static string Rendered(int used) => used <= Tokens
        ? string.Create(CultureInfo.InvariantCulture, $"\nbudget={Tokens} used={used} left={Tokens - used}")
        : string.Create(CultureInfo.InvariantCulture, $"\nbudget={Tokens} used={used} over={used - Tokens}");
}
