namespace TerseSharp.Core;

public static class RazorMarkup
{
    public static int CloseOf(string text, RazorElement element)
    {
        if (element.SelfClosing)
            return element.Span.End;

        var close = "</" + element.TagName + ">";
        var open = "<" + element.TagName;
        var depth = 0;
        var cursor = element.Span.End;

        while (cursor < text.Length)
        {
            var nextClose = text.IndexOf(close, cursor, StringComparison.Ordinal);

            if (nextClose < 0)
                return element.Span.End;

            var nextOpen = Nested(text, cursor, open, nextClose);

            if (nextOpen >= 0)
            {
                depth++;
                cursor = nextOpen + open.Length;

                continue;
            }

            if (depth is 0)
                return nextClose + close.Length;

            depth--;
            cursor = nextClose + close.Length;
        }

        return element.Span.End;
    }

    private static int Nested(string text, int from, string open, int limit)
    {
        for (var index = text.IndexOf(open, from, StringComparison.Ordinal); index >= 0 && index < limit;)
        {
            var after = index + open.Length;

            if (after >= text.Length || text[after] is ' ' or '\t' or '\r' or '\n' or '>' or '/')
                return index;

            index = text.IndexOf(open, after, StringComparison.Ordinal);
        }

        return -1;
    }
}
