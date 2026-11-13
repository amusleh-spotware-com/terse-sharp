namespace TerseSharp.Server;

internal static class BraceExpansion
{
    private const int MaxAlternatives = 32;

    private const string Fence = ".";

    public static void Into(string token, List<string> operands)
    {
        var budget = MaxAlternatives;

        Alternated(token, operands, ref budget);
    }

    public static string Climbed(string path) =>
        path.Contains("/..", StringComparison.Ordinal) ? Path.GetFullPath(path)[2..].Replace('\\', '/') : path;

    private static void Alternated(string token, List<string> operands, ref int budget)
    {
        var open = budget > 0 ? BraceOpen(token) : -1;

        if (open < 0)
        {
            Alternative(token, operands, ref budget);

            return;
        }

        var close = Matching(token, open);
        var from = open + 1;

        while (from <= close)
        {
            var comma = NextComma(token, from, close);

            Alternated(string.Concat(token.AsSpan(0, open), token.AsSpan(from, comma - from), token.AsSpan(close + 1)), operands, ref budget);
            from = comma + 1;
        }
    }

    private static void Alternative(string token, List<string> operands, ref int budget)
    {
        if (budget-- > 0)
            operands.Add(token);
        else if (budget is -1)
            operands.Add(Fence);
    }

    private static int BraceOpen(string token)
    {
        for (var open = token.IndexOf('{', StringComparison.Ordinal); open >= 0; open = token.IndexOf('{', open + 1))
        {
            if (Lists(token, open))
                return open;
        }

        return -1;
    }

    private static bool Lists(string token, int open) =>
        token.AsSpan(0, open) is not [.., '$'] && Commaed(token, open, Matching(token, open));

    private static bool Commaed(string token, int open, int close) => close > open && NextComma(token, open + 1, close) < close;

    private static int Matching(string token, int open)
    {
        var depth = 0;

        for (var index = open; index < token.Length; index++)
        {
            depth += Depth(token[index]);

            if (depth is 0)
                return index;
        }

        return -1;
    }

    private static int NextComma(string token, int from, int close)
    {
        var depth = 0;

        for (var index = from; index < close; index++)
        {
            depth += Depth(token[index]);

            if (depth is 0 && token[index] is ',')
                return index;
        }

        return close;
    }

    private static int Depth(char character) => character switch
    {
        '{' => 1,
        '}' => -1,
        _ => 0,
    };
}
