namespace TerseSharp.Server;

public static partial class ToolGuard
{
    private static bool Braced(string text) =>
        text.Contains('{', StringComparison.Ordinal)
        && Array.Exists(Words(text), word => word.Contains('{', StringComparison.Ordinal) && Alternatives(word).Exists(expansion => Tokens(expansion).Any(IsDotNet)));

    private static List<string> Alternatives(string word)
    {
        var expansions = new List<string>(2);

        BraceExpansion.Into(word.Trim('"', '\''), expansions);

        return expansions;
    }
}
