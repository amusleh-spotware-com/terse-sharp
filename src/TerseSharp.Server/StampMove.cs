namespace TerseSharp.Server;

public static class StampMove
{
    public static string Named(string remembered, string current)
    {
        var before = remembered.AsSpan();
        var after = current.AsSpan();

        while (before.Length > 0 && after.Length > 0)
        {
            var left = Head(ref before);
            var right = Head(ref after);

            if (!left.SequenceEqual(right))
                return Component(left, right);
        }

        return string.Create(CultureInfo.InvariantCulture, $"segments {remembered.AsSpan().Count(' ') + 1}->{current.AsSpan().Count(' ') + 1}");
    }

    public static string Appended(string text, string? note) =>
        note is null ? text
            : text.EndsWith('\n') ? string.Concat(text, note)
            : string.Concat(text, "\n", note);

    private static ReadOnlySpan<char> Head(ref ReadOnlySpan<char> text)
    {
        var cut = text.IndexOf(' ');
        var head = cut < 0 ? text : text[..cut];
        text = cut < 0 ? [] : text[(cut + 1)..];

        return head;
    }

    private static string Component(ReadOnlySpan<char> before, ReadOnlySpan<char> after)
    {
        var beforeCut = before.LastIndexOf('=');
        var afterCut = after.LastIndexOf('=');

        return beforeCut < 0 || afterCut < 0
            ? string.Create(CultureInfo.InvariantCulture, $"'{before}'->'{after}'")
            : Headed(before[..beforeCut], after[..afterCut], before[(beforeCut + 1)..], after[(afterCut + 1)..]);
    }

    private static string Headed(ReadOnlySpan<char> beforeHead, ReadOnlySpan<char> afterHead, ReadOnlySpan<char> beforeValue, ReadOnlySpan<char> afterValue)
    {
        var beforeAt = beforeHead.LastIndexOf('@');
        var afterAt = afterHead.LastIndexOf('@');

        return beforeHead.SequenceEqual(afterHead)
            ? string.Create(CultureInfo.InvariantCulture, $"{(beforeAt < 0 ? beforeHead : "generations")} {beforeValue}->{afterValue}")
            : beforeAt >= 0 && afterAt >= 0 && beforeHead[..beforeAt].SequenceEqual(afterHead[..afterAt])
                ? string.Create(CultureInfo.InvariantCulture, $"LoadedUtc {beforeHead[(beforeAt + 1)..]}->{afterHead[(afterAt + 1)..]}")
                : string.Create(CultureInfo.InvariantCulture, $"workspace {beforeHead}->{afterHead}");
    }
}
